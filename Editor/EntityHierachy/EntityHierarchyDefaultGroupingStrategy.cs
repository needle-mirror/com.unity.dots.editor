using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Transforms;
using UnityEditor;

namespace Unity.Entities.Editor
{
    class EntityHierarchyDefaultGroupingStrategy : IEntityHierarchyGroupingStrategy
    {
        const int k_DefaultNodeCapacity = 1024;
        const int k_DefaultChildrenCapacity = 8;

        static readonly string k_UnknownSceneName = L10n.Tr("<UnknownScene>");

        readonly int m_ChildTypeIndex;
        readonly World m_World;

        // Note: A performance issue with iterating over NativeHashMaps with medium to large capacity (regardless of the count) forces us to use Dictionaries here.
        // This prevents burstability and jobification, but it's also a 10+x speedup in the Boids sample, when there is no changes to compute.
        // We should go back to NativeHashMap if / when this performance issue is addressed.
        readonly Dictionary<EntityHierarchyNodeId, AddOperation> m_AddedNodes = new Dictionary<EntityHierarchyNodeId, AddOperation>(k_DefaultNodeCapacity);
        readonly Dictionary<EntityHierarchyNodeId, MoveOperation> m_MovedNodes = new Dictionary<EntityHierarchyNodeId, MoveOperation>(k_DefaultNodeCapacity);
        readonly Dictionary<EntityHierarchyNodeId, RemoveOperation> m_RemovedNodes = new Dictionary<EntityHierarchyNodeId, RemoveOperation>(k_DefaultNodeCapacity);

        NativeHashMap<Entity, SceneTag> m_SceneTagPerEntity = new NativeHashMap<Entity, SceneTag>(k_DefaultNodeCapacity, Allocator.Persistent);

        NativeHashMap<EntityHierarchyNodeId, Entity> m_EntityNodes = new NativeHashMap<EntityHierarchyNodeId, Entity>(k_DefaultNodeCapacity, Allocator.Persistent);
        NativeHashMap<EntityHierarchyNodeId, Hash128> m_SceneNodes = new NativeHashMap<EntityHierarchyNodeId, Hash128>(k_DefaultNodeCapacity, Allocator.Persistent);
        // TODO: Replace with NativeHashSet when available + need to burst this
        readonly HashSet<Entity> m_KnownMissingParent = new HashSet<Entity>();

        NativeHashMap<EntityHierarchyNodeId, uint> m_Versions = new NativeHashMap<EntityHierarchyNodeId, uint>(k_DefaultNodeCapacity, Allocator.Persistent);
        NativeHashMap<EntityHierarchyNodeId, EntityHierarchyNodeId> m_Parents = new NativeHashMap<EntityHierarchyNodeId, EntityHierarchyNodeId>(k_DefaultNodeCapacity, Allocator.Persistent);
        NativeHashMap<EntityHierarchyNodeId, UnsafeHashMap<EntityHierarchyNodeId, byte>> m_Children = new NativeHashMap<EntityHierarchyNodeId, UnsafeHashMap<EntityHierarchyNodeId, byte>>(k_DefaultNodeCapacity, Allocator.Persistent);

        EntityQuery m_RootEntitiesQuery;
        EntityQuery m_ParentEntitiesQuery;
        EntityQueryMask m_RootEntitiesQueryMask;
        EntityQueryMask m_ParentEntitiesQueryMask;

        public EntityHierarchyDefaultGroupingStrategy(World world)
        {
            m_ChildTypeIndex = TypeManager.GetTypeIndex(typeof(Child));

            m_World = world;
            m_Versions.Add(EntityHierarchyNodeId.Root, 0);
            m_Children.Add(EntityHierarchyNodeId.Root, new UnsafeHashMap<EntityHierarchyNodeId, byte>(k_DefaultChildrenCapacity, Allocator.Persistent));

            m_RootEntitiesQuery = m_World.EntityManager.CreateEntityQuery(new EntityQueryDesc { None = new ComponentType[] { typeof(Parent) } });
            m_ParentEntitiesQuery = m_World.EntityManager.CreateEntityQuery(new EntityQueryDesc { All = new ComponentType[] { typeof(Child) } });
            m_RootEntitiesQueryMask = m_World.EntityManager.GetEntityQueryMask(m_RootEntitiesQuery);
            m_ParentEntitiesQueryMask = m_World.EntityManager.GetEntityQueryMask(m_ParentEntitiesQuery);
        }

        public void Dispose()
        {
            if (m_World.IsCreated)
            {
                if (m_World.EntityManager.IsQueryValid(m_RootEntitiesQuery))
                    m_RootEntitiesQuery.Dispose();

                if (m_World.EntityManager.IsQueryValid(m_ParentEntitiesQuery))
                    m_ParentEntitiesQuery.Dispose();
            }

            m_SceneTagPerEntity.Dispose();
            m_EntityNodes.Dispose();
            m_SceneNodes.Dispose();

            m_Versions.Dispose();
            m_Parents.Dispose();
            new FreeChildrenListsJob { ChildrenLists = m_Children.GetValueArray(Allocator.TempJob) }.Run();
            m_Children.Dispose();
        }

        public ComponentType[] ComponentsToWatch { get; } = { typeof(Parent), typeof(SceneTag) };

        void IEntityHierarchyGroupingStrategy.BeginApply(IEntityHierarchyGroupingContext context)
        {
            m_AddedNodes.Clear();
            m_MovedNodes.Clear();
            m_RemovedNodes.Clear();
        }

        void IEntityHierarchyGroupingStrategy.ApplyEntityChanges(NativeArray<Entity> newEntities, NativeArray<Entity> removedEntities, IEntityHierarchyGroupingContext context)
        {
            // Remove entities
            foreach (var entity in removedEntities)
                RegisterRemoveOperation(entity);

            // Add new entities
            foreach (var entity in newEntities)
                RegisterAddOperation(entity);

            UpdateMissingParentEntities();
            MoveEntitiesUnderFoundMissingParents();
        }

        void IEntityHierarchyGroupingStrategy.ApplyComponentDataChanges(ComponentType componentType, in ComponentDataDiffer.ComponentChanges componentChanges, IEntityHierarchyGroupingContext context)
        {
            if (componentType == typeof(Parent))
                ApplyParentComponentChanges(componentChanges);
        }

        void IEntityHierarchyGroupingStrategy.ApplySharedComponentDataChanges(ComponentType componentType, in SharedComponentDataDiffer.ComponentChanges componentChanges, IEntityHierarchyGroupingContext context)
        {
            if (componentType == typeof(SceneTag))
                ApplySceneTagChanges(componentChanges, context);
        }

        bool IEntityHierarchyGroupingStrategy.EndApply(IEntityHierarchyGroupingContext context)
        {
            // NOTE - Order matters:
            // 1.Removed - can add move operation, when a parent is removed, children are moved under root
            // 2.Added
            // 3.Moved
            // 4.Scene Mapping

            var hasAdditions = m_AddedNodes.Count > 0;
            var hasRemovals = m_RemovedNodes.Count > 0;

            foreach (var node in m_RemovedNodes.Keys)
                RemoveNode(node, context.Version);

            var hasMoves = m_MovedNodes.Count > 0;

            foreach (var kvp in m_AddedNodes)
            {
                var node = kvp.Key;
                var operation = kvp.Value;
                AddNode(operation.Parent, node, context.Version);
                m_EntityNodes[node] = operation.Entity;
            }

            foreach (var kvp in m_MovedNodes)
            {
                var node = kvp.Key;
                var operation = kvp.Value;
                MoveNode(operation.FromNode, operation.ToNode, node, context);
            }

            if (hasRemovals || hasMoves)
            {
                if (TryRemoveEmptySceneNodes(NodeKind.SubScene, context.Version))
                    TryRemoveEmptySceneNodes(NodeKind.Scene, context.Version);
            }

            return hasAdditions || hasMoves || hasRemovals;
        }

        bool TryRemoveEmptySceneNodes(NodeKind sceneKindToProcess, uint version)
        {
            var sceneNodesChanged = false;
            var sceneNodes = m_SceneNodes.GetKeyArray(Allocator.Temp);
            for (var i = 0; i < sceneNodes.Length; i++)
            {
                var sceneNode = sceneNodes[i];
                if (sceneNode.Kind != sceneKindToProcess || HasChildren(sceneNode))
                    continue;

                m_SceneNodes.Remove(sceneNode);
                RemoveNode(sceneNode, version);
                sceneNodesChanged = true;
            }
            sceneNodes.Dispose();

            return sceneNodesChanged;
        }

        public bool HasChildren(in EntityHierarchyNodeId nodeId)
            => m_Children.TryGetValue(nodeId, out var l) && l.Count() > 0;

        public NativeArray<EntityHierarchyNodeId> GetChildren(in EntityHierarchyNodeId nodeId, Allocator allocator)
            => m_Children[nodeId].GetKeyArray(allocator);

        public bool Exists(in EntityHierarchyNodeId nodeId)
            => m_Versions.ContainsKey(nodeId);

        public Entity GetUnderlyingEntity(in EntityHierarchyNodeId nodeId)
        {
            if (nodeId.Kind != NodeKind.Entity)
                throw new NotSupportedException();

            return m_EntityNodes.TryGetValue(nodeId, out var entity) ? entity : Entity.Null;
        }

        public uint GetNodeVersion(in EntityHierarchyNodeId nodeId)
            => m_Versions[nodeId];

        public string GetNodeName(in EntityHierarchyNodeId nodeId)
        {
            switch (nodeId.Kind)
            {
                case NodeKind.Scene:
                case NodeKind.SubScene:
                {
                    var sceneHash = GetSceneHash(nodeId);
                    var loadedSceneRef = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(sceneHash.ToString()));
                    return loadedSceneRef == null ? k_UnknownSceneName : loadedSceneRef.name;
                }
                case NodeKind.Entity:
                {
                    if (!m_EntityNodes.TryGetValue(nodeId, out var entity))
                        return nodeId.ToString();

                    var name = m_World.EntityManager.GetName(entity);
                    return string.IsNullOrEmpty(name) ? entity.ToString() : name;
                }
                default:
                {
                    throw new NotSupportedException();
                }
            }
        }

        void UpdateMissingParentEntities()
        {
            if (m_RemovedNodes.Count == 0)
                return;

            // Get a native array of entities to actually remove
            var filteredRemovedEntities = new NativeList<Entity>(m_RemovedNodes.Count, Allocator.TempJob);
            foreach (var removedNode in m_RemovedNodes.Values)
            {
                filteredRemovedEntities.Add(removedNode.Entity);
            }

            // Filter only the entities with a Child component
            var removedParentEntities = new NativeList<Entity>(Allocator.TempJob);
            new FilterEntitiesWithQueryMask
            {
                QueryMask = m_ParentEntitiesQueryMask,
                Source = filteredRemovedEntities,
                Result = removedParentEntities
            }.Run();

            filteredRemovedEntities.Dispose();

            // Aggregate with all known missing parent cache
            for (var i = 0; i < removedParentEntities.Length; i++)
            {
                m_KnownMissingParent.Add(removedParentEntities[i]);
            }

            removedParentEntities.Dispose();
        }

        unsafe void MoveEntitiesUnderFoundMissingParents()
        {
            if (m_AddedNodes.Count == 0)
                return;

            // Find all missing parent in this changeset
            var missingParentDetectedInThisBatch = new NativeList<Entity>(Allocator.TempJob);
            foreach (var addedNode in m_AddedNodes.Values)
            {
                if (!m_KnownMissingParent.Remove(addedNode.Entity))
                    continue;

                missingParentDetectedInThisBatch.Add(addedNode.Entity);
            }

            // Find all children for each newly found missing parent
            if (missingParentDetectedInThisBatch.Length > 0)
            {
                var childrenPerParent = new NativeArray<UnsafeList<Entity>>(missingParentDetectedInThisBatch.Length, Allocator.TempJob);
                var bufferAccessor = m_World.EntityManager.GetBufferFromEntity<Child>(true);

                new FindAllChildrenOfEntity
                {
                    EntityComponentStore = m_World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore,
                    ChildTypeIndex = m_ChildTypeIndex,
                    BufferAccessor = bufferAccessor,
                    ChildrenPerParent = childrenPerParent,
                    NewFoundParent = missingParentDetectedInThisBatch
                }.Schedule(missingParentDetectedInThisBatch.Length, 1).Complete();

                // Remap children to formerly missing parent
                for (var i = 0; i < missingParentDetectedInThisBatch.Length; i++)
                {
                    var children = childrenPerParent[i];
                    if (!children.IsCreated)
                        continue;

                    var parent = EntityHierarchyNodeId.FromEntity(missingParentDetectedInThisBatch[i]);
                    for (var j = 0; j < children.Length; j++)
                    {
                        var child = children[j];
                        var childNodeId = EntityHierarchyNodeId.FromEntity(child);
                        if (m_EntityNodes.ContainsKey(childNodeId))
                            RegisterMoveOperation(parent, childNodeId);
                    }

                    children.Dispose();
                }

                childrenPerParent.Dispose();
            }

            missingParentDetectedInThisBatch.Dispose();
        }

        void ApplyParentComponentChanges(ComponentDataDiffer.ComponentChanges componentChanges)
        {
            // parent removed
            if (componentChanges.RemovedComponentsCount > 0)
            {
                var(entities, parents) = componentChanges.GetRemovedComponents<Parent>(Allocator.TempJob);
                for (var i = 0; i < componentChanges.RemovedComponentsCount; i++)
                {
                    var entityNodeId = EntityHierarchyNodeId.FromEntity(entities[i]);
                    RegisterMoveOperation(EntityHierarchyNodeId.Root, entityNodeId);
                }

                entities.Dispose();
                parents.Dispose();
            }

            // parent added
            if (componentChanges.AddedComponentsCount > 0)
            {
                var(entities, parents) = componentChanges.GetAddedComponents<Parent>(Allocator.TempJob);
                for (var i = 0; i < componentChanges.AddedComponentsCount; i++)
                {
                    var entity = entities[i];
                    var entityNodeId = EntityHierarchyNodeId.FromEntity(entity);
                    var newParentComponent = parents[i];
                    var newParentEntity = newParentComponent.Value;
                    var newParentEntityNodeId = EntityHierarchyNodeId.FromEntity(newParentEntity);

                    if (!m_EntityNodes.ContainsKey(newParentEntityNodeId) && !m_AddedNodes.ContainsKey(newParentEntityNodeId))
                    {
                        m_KnownMissingParent.Add(newParentEntity);
                        RegisterMoveOperation(EntityHierarchyNodeId.Root, entityNodeId);
                    }
                    else
                        RegisterMoveOperation(newParentEntityNodeId, entityNodeId);
                }

                entities.Dispose();
                parents.Dispose();
            }
        }

        void ApplySceneTagChanges(SharedComponentDataDiffer.ComponentChanges componentChanges, IEntityHierarchyGroupingContext context)
        {
            for (var i = 0; i < componentChanges.RemovedEntitiesCount; ++i)
            {
                var entity = componentChanges.GetRemovedEntity(i);
                m_SceneTagPerEntity.Remove(entity);
                if (!m_RootEntitiesQueryMask.Matches(entity))
                    continue;

                var tag = componentChanges.GetRemovedComponent<SceneTag>(i);

                var entityNodeId = EntityHierarchyNodeId.FromEntity(entity);

                var subsceneHash = context.SceneMapper.GetSubsceneHash(m_World, tag.SceneEntity);
                if (subsceneHash == default)
                    continue; // Previous parent was not a scene or was a scene that does not exist anymore; skip

                // If this is not the first move, this entity didn't have a parent before and now it does, skip!
                RegisterFirstMoveOperation(EntityHierarchyNodeId.Root, entityNodeId);
            }

            for (var i = 0; i < componentChanges.AddedEntitiesCount; ++i)
            {
                var entity = componentChanges.GetAddedEntity(i);
                var entityNodeId = EntityHierarchyNodeId.FromEntity(entity);
                var tag = componentChanges.GetAddedComponent<SceneTag>(i);
                m_SceneTagPerEntity[entity] = tag;

                if (m_RootEntitiesQueryMask.Matches(entity) || m_AddedNodes.TryGetValue(entityNodeId, out var addOperation) && addOperation.Parent == EntityHierarchyNodeId.Root)
                {
                    var subsceneHash = context.SceneMapper.GetSubsceneHash(m_World, tag.SceneEntity);
                    var newParentNodeId = subsceneHash == default ? EntityHierarchyNodeId.Root : GetOrCreateSubsceneNode(subsceneHash, context);
                    RegisterMoveOperation(newParentNodeId, entityNodeId);
                }
            }
        }

        Hash128 GetSceneHash(in EntityHierarchyNodeId nodeId)
            => m_SceneNodes[nodeId];

        EntityHierarchyNodeId GetOrCreateSubsceneNode(Hash128 subSceneHash, IEntityHierarchyGroupingContext context)
        {
            if (!context.SceneMapper.TryGetSceneOrSubSceneInstanceId(subSceneHash, out var subSceneInstanceId))
            {
                Debug.LogWarning($"SubScene hash {subSceneHash} not found in {nameof(SceneMapper)} state, unable to create node id for it.");
                return default;
            }

            var subSceneNodeId = EntityHierarchyNodeId.FromSubScene(subSceneInstanceId);
            if (!Exists(subSceneNodeId))
            {
                var parentSceneHash = context.SceneMapper.GetParentSceneHash(subSceneHash);
                if (!context.SceneMapper.TryGetSceneOrSubSceneInstanceId(parentSceneHash, out var parentSceneInstanceId))
                {
                    Debug.LogWarning($"Scene hash {parentSceneHash} not found in {nameof(SceneMapper)} state, unable to create node id for it.");
                    return default;
                }

                var parentSceneNodeId = EntityHierarchyNodeId.FromScene(parentSceneInstanceId);
                if (!Exists(parentSceneNodeId))
                {
                    m_SceneNodes[parentSceneNodeId] = parentSceneHash;
                    AddNode(EntityHierarchyNodeId.Root, parentSceneNodeId, context.Version);
                }

                m_SceneNodes[subSceneNodeId] = subSceneHash;
                AddNode(parentSceneNodeId, subSceneNodeId, context.Version);
            }

            return subSceneNodeId;
        }

        void RegisterAddOperation(Entity entity)
        {
            var node = EntityHierarchyNodeId.FromEntity(entity);
            if (m_RemovedNodes.ContainsKey(node))
                m_RemovedNodes.Remove(node);
            else
                m_AddedNodes[node] = new AddOperation {Entity = entity, Parent = EntityHierarchyNodeId.Root};
        }

        void RegisterRemoveOperation(Entity entity)
        {
            var node = EntityHierarchyNodeId.FromEntity(entity);
            if (m_AddedNodes.ContainsKey(node))
                m_AddedNodes.Remove(node);
            else
                m_RemovedNodes[node] = new RemoveOperation { Entity = entity };
        }

        void RegisterMoveOperation(EntityHierarchyNodeId toNode, EntityHierarchyNodeId node)
        {
            var previousParentNodeId = m_Parents.ContainsKey(node) ? m_Parents[node] : default;
            RegisterMoveOperation(previousParentNodeId, toNode, node);
        }

        void RegisterMoveOperation(EntityHierarchyNodeId fromNode, EntityHierarchyNodeId toNode, EntityHierarchyNodeId node)
        {
            if (m_RemovedNodes.ContainsKey(node))
                return;

            // Move a node to root if the intended parent does not exist and will not be created in this batch
            var destinationNode = Exists(toNode) || m_AddedNodes.ContainsKey(toNode) ? toNode : EntityHierarchyNodeId.Root;

            if (m_AddedNodes.ContainsKey(node))
            {
                var addOperation = m_AddedNodes[node];
                addOperation.Parent = destinationNode;
                m_AddedNodes[node] = addOperation;
            }
            else if (m_MovedNodes.ContainsKey(node))
            {
                var moveOperation = m_MovedNodes[node];
                moveOperation.ToNode = destinationNode;
                m_MovedNodes[node] = moveOperation;
            }
            else
            {
                m_MovedNodes[node] = new MoveOperation { FromNode = fromNode, ToNode = destinationNode };
            }
        }

        // Only register a move operation if this is the first move detected
        void RegisterFirstMoveOperation(EntityHierarchyNodeId toNode, EntityHierarchyNodeId node)
        {
            if (m_MovedNodes.ContainsKey(node))
                return;

            RegisterMoveOperation(toNode, node);
        }

        void AddNode(in EntityHierarchyNodeId parentNode, in EntityHierarchyNodeId newNode, uint version)
        {
            if (parentNode.Equals(default))
                throw new ArgumentException("Trying to add a new node to an invalid parent node.");

            if (newNode.Equals(default))
                throw new ArgumentException("Trying to add an invalid node to the tree.");

            m_Versions[newNode] = version;
            m_Versions[parentNode] = version;
            m_Parents[newNode] = parentNode;

            AddChild(m_Children, parentNode, newNode);
        }

        void RemoveNode(in EntityHierarchyNodeId node, uint version)
        {
            if (node.Equals(default))
                throw new ArgumentException("Trying to remove an invalid node from the tree.");

            m_Versions.Remove(node);

            if (node.Kind == NodeKind.Entity)
                m_EntityNodes.Remove(node);

            if (!m_Parents.TryGetValue(node, out var parentNodeId))
                return;

            m_Parents.Remove(node);
            m_Versions[parentNodeId] = version;
            RemoveChild(m_Children, parentNodeId, node);

            // Move all children of the removed node to root
            if (m_Children.TryGetValue(node, out var children))
            {
                // Note: List might be too large for Temp allocator size limit of 16kb
                using (var childrenNodes = children.GetKeyArray(Allocator.TempJob))
                {
                    for (int i = 0, n = children.Count(); i < n; i++)
                    {
                        RegisterMoveOperation(node, EntityHierarchyNodeId.Root, childrenNodes[i]);
                    }
                }
            }
        }

        void MoveNode(in EntityHierarchyNodeId previousParent, EntityHierarchyNodeId newParent, in EntityHierarchyNodeId node, IEntityHierarchyGroupingContext context)
        {
            if (previousParent.Equals(default))
                throw new ArgumentException("Trying to unparent from an invalid node.");

            if (newParent.Equals(default))
                throw new ArgumentException("Trying to parent to an invalid node.");

            if (node.Equals(default))
                throw new ArgumentException("Trying to add an invalid node to the tree.");

            if (previousParent.Equals(newParent))
                return; // NOOP

            if (m_Parents[node] == newParent)
                return; // NOOP

            if (node.Kind == NodeKind.Entity
                && newParent == EntityHierarchyNodeId.Root
                && m_SceneTagPerEntity.TryGetValue(m_EntityNodes[node], out var tag))
            {
                var subsceneHash = context.SceneMapper.GetSubsceneHash(m_World, tag.SceneEntity);

                newParent = subsceneHash == default ? EntityHierarchyNodeId.Root : GetOrCreateSubsceneNode(subsceneHash, context);
            }

            RemoveChild(m_Children, previousParent, node);
            if (Exists(previousParent))
                m_Versions[previousParent] = context.Version;

            m_Parents[node] = newParent;
            AddChild(m_Children, newParent, node);
            m_Versions[newParent] = context.Version;
        }

        static void AddChild(NativeHashMap<EntityHierarchyNodeId, UnsafeHashMap<EntityHierarchyNodeId, byte>> children, in EntityHierarchyNodeId parentId, in EntityHierarchyNodeId newChild)
        {
            if (!children.TryGetValue(parentId, out var siblings))
                siblings = new UnsafeHashMap<EntityHierarchyNodeId, byte>(k_DefaultChildrenCapacity, Allocator.Persistent);

            siblings.Add(newChild, 0);
            children[parentId] = siblings;
        }

        static void RemoveChild(NativeHashMap<EntityHierarchyNodeId, UnsafeHashMap<EntityHierarchyNodeId, byte>> children, in EntityHierarchyNodeId parentId, in EntityHierarchyNodeId childToRemove)
        {
            if (!children.TryGetValue(parentId, out var siblings))
                return;

            siblings.Remove(childToRemove);
            children[parentId] = siblings;
        }

        struct AddOperation
        {
            public EntityHierarchyNodeId Parent;
            public Entity Entity;
        }

        struct MoveOperation
        {
            public EntityHierarchyNodeId FromNode;
            public EntityHierarchyNodeId ToNode;
        }

        struct RemoveOperation
        {
            public Entity Entity;
        }

        [BurstCompile]
        struct FilterEntitiesWithQueryMask : IJob
        {
            [ReadOnly] public EntityQueryMask QueryMask;
            [ReadOnly] public NativeArray<Entity> Source;
            [WriteOnly] public NativeList<Entity> Result;

            public void Execute()
            {
                for (var i = 0; i < Source.Length; i++)
                {
                    var entity = Source[i];
                    if (QueryMask.Matches(entity))
                        Result.Add(entity);
                }
            }
        }

        [BurstCompile]
        unsafe struct FindAllChildrenOfEntity : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* EntityComponentStore;
            public int ChildTypeIndex;

            [ReadOnly] public BufferFromEntity<Child> BufferAccessor;
            [ReadOnly] public NativeArray<Entity> NewFoundParent;
            [WriteOnly] public NativeArray<UnsafeList<Entity>> ChildrenPerParent;

            public void Execute(int index)
            {
                var entity = NewFoundParent[index];
                if (EntityComponentStore->HasComponent(entity, ChildTypeIndex))
                {
                    var b = BufferAccessor[entity];
                    var children = new UnsafeList<Entity>(b.Length, Allocator.TempJob);
                    for (var i = 0; i < b.Length; i++)
                    {
                        children.Add(b[i].Value);
                    }

                    ChildrenPerParent[index] = children;
                }
            }
        }

        [BurstCompile]
        struct FreeChildrenListsJob : IJob
        {
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<UnsafeHashMap<EntityHierarchyNodeId, byte>> ChildrenLists;

            public void Execute()
            {
                for (var i = 0; i < ChildrenLists.Length; i++)
                {
                    ChildrenLists[i].Dispose();
                }
            }
        }
    }
}
