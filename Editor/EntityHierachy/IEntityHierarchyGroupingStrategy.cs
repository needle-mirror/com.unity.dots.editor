using JetBrains.Annotations;
using System;
using Unity.Collections;

namespace Unity.Entities.Editor
{
    interface IEntityHierarchyGroupingContext
    {
        uint Version { get; }
        ISceneMapper SceneMapper { get; }
    }

    interface IEntityHierarchyGroupingStrategy : IDisposable
    {
        ComponentType[] ComponentsToWatch { get; }

        void BeginApply(IEntityHierarchyGroupingContext context);
        void ApplyEntityChanges(NativeArray<Entity> newEntities, NativeArray<Entity> removedEntities, IEntityHierarchyGroupingContext context);
        void ApplyComponentDataChanges(ComponentType componentType, in ComponentDataDiffer.ComponentChanges componentChanges, IEntityHierarchyGroupingContext context);
        void ApplySharedComponentDataChanges(ComponentType componentType, in SharedComponentDataDiffer.ComponentChanges componentChanges, IEntityHierarchyGroupingContext context);
        bool EndApply(IEntityHierarchyGroupingContext context);

        bool HasChildren(in EntityHierarchyNodeId nodeId);

        NativeArray<EntityHierarchyNodeId> GetChildren(in EntityHierarchyNodeId nodeId, Allocator allocator);

        bool Exists(in EntityHierarchyNodeId nodeId);

        Entity GetUnderlyingEntity(in EntityHierarchyNodeId nodeId);

        uint GetNodeVersion(in EntityHierarchyNodeId nodeId);

        string GetNodeName(in EntityHierarchyNodeId nodeId);
    }

    struct VirtualTreeNode
    {
        [UsedImplicitly]
        public Hash128 SceneId;
    }
}
