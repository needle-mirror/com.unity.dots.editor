using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Editor.Bridge;
using Unity.Scenes;

namespace Unity.Entities.Editor
{
    class SystemTreeViewItem : ITreeViewItem
    {
        internal static readonly BasicPool<SystemTreeViewItem> Pool = new BasicPool<SystemTreeViewItem>(() => new SystemTreeViewItem());

        readonly List<ITreeViewItem> m_CachedChildren = new List<ITreeViewItem>();
        public IPlayerLoopNode Node;
        public PlayerLoopSystemGraph Graph;
        public World World;

        SystemTreeViewItem() { }

        public static SystemTreeViewItem Acquire(PlayerLoopSystemGraph graph, IPlayerLoopNode node, SystemTreeViewItem parent, World world)
        {
            var item = Pool.Acquire();

            item.World = world;
            item.Graph = graph;
            item.Node = node;
            item.parent = parent;

            return item;
        }

        public SystemHandle SystemHandle
        {
            get
            {
                if (Node is ISystemHandleNode systemHandleNode)
                    return systemHandleNode.SystemHandle;

                return null;
            }
        }

        public bool HasChildren => Node.Children.Count > 0;

        public string GetSystemName(World world = null)
        {
            if (world == null ||
                (Node is ISystemHandleNode systemHandleNode && systemHandleNode.SystemHandle.World.Name != world.Name))
            {
                return Node.NameWithWorld;
            }

            return Node?.Name;
        }

        public bool GetParentState()
        {
            return Node.EnabledInHierarchy;
        }

        public void SetPlayerLoopSystemState(bool state)
        {
            Node.Enabled = state;
        }

        public void SetSystemState(bool state)
        {
            if (Node.Enabled == state)
                return;

            Node.Enabled = state;
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        public unsafe string GetEntityMatches()
        {
            if (HasChildren) // Group system do not need entity matches.
                return string.Empty;

            var ptr = SystemHandle.StatePointer;
            if (ptr == null)
                return string.Empty;

            var matchedEntityCount = string.Empty;
            if (!Node.Enabled || !NodeParentsAllEnabled(Node))
            {
                matchedEntityCount = Constants.SystemSchedule.k_Dash;
            }
            else
            {
                var entityQueries = ptr->EntityQueries;
                var entityCountSum = 0;
                for (var i = 0; i < entityQueries.length; i++)
                {
                    entityCountSum += entityQueries[i].CalculateEntityCount();
                }

                matchedEntityCount = entityCountSum.ToString();
            }

            return matchedEntityCount;
        }

        float GetAverageRunningTime(SystemHandle systemHandle, SystemHandle parentSystemHandle)
        {
            if (systemHandle.Managed != null && systemHandle.Managed is ComponentSystemGroup systemGroup)
            {
                if (systemGroup.Systems != null)
                {
                    var managedChildSystemsSum = systemGroup.Systems.Sum(child => GetAverageRunningTime(child, systemGroup));

                    // unmanaged system
                    var unmanagedChildSystems = systemGroup.UnmanagedSystems;
                    var unmanagedChildSystemSum = 0.0f;
                    for (var i = 0; i < unmanagedChildSystems.length; i++)
                    {
                        unmanagedChildSystemSum += GetAverageRunningTime(new SystemHandle(unmanagedChildSystems[i], systemGroup.World), systemGroup);
                    }

                    return managedChildSystemsSum + unmanagedChildSystemSum;
                }
            }
            else
            {
                var recorderKey = new PlayerLoopSystemGraph.RecorderKey
                {
                    World = systemHandle.World,
                    Group = parentSystemHandle.Managed as ComponentSystemGroup,
                    SystemHandle = systemHandle
                };

                return Graph.RecordersBySystem.TryGetValue(recorderKey, out var recorder) ? recorder.ReadMilliseconds() : 0.0f;
            }

            return -1;
        }

        public string GetRunningTime()
        {
            var totalTime = string.Empty;

            if (Node is IPlayerLoopSystemData)
                return string.Empty;

            if (children.Any())
            {
                totalTime = !Node.Enabled || !NodeParentsAllEnabled(Node)
                    ? Constants.SystemSchedule.k_Dash
                    : Node.Children.OfType<ISystemHandleNode>().Sum(child => GetAverageRunningTime(child.SystemHandle, SystemHandle)).ToString("f2");
            }
            else
            {
                if (Node.IsRunning && Node is ISystemHandleNode data && Node.Parent is ComponentGroupNode componentGroupNode)
                {
                    var parentSystem = componentGroupNode.SystemHandle;
                    totalTime = !Node.Enabled || !NodeParentsAllEnabled(Node)
                        ? Constants.SystemSchedule.k_Dash
                        : GetAverageRunningTime(data.SystemHandle, parentSystem).ToString("f2");
                }
                else
                {
                    return Constants.SystemSchedule.k_Dash;
                }
            }

            return totalTime;
        }

        bool NodeParentsAllEnabled(IPlayerLoopNode node)
        {
            if (node.Parent != null)
            {
                if (!node.Parent.Enabled) return false;
                if (!NodeParentsAllEnabled(node.Parent)) return false;
            }

            return true;
        }

        public int id => Node.Hash;
        public ITreeViewItem parent { get; internal set; }
        public IEnumerable<ITreeViewItem> children => m_CachedChildren;
        bool ITreeViewItem.hasChildren => HasChildren;

        public void AddChild(ITreeViewItem child)
        {
            throw new NotImplementedException();
        }

        public void AddChildren(IList<ITreeViewItem> children)
        {
            throw new NotImplementedException();
        }

        public void RemoveChild(ITreeViewItem child)
        {
            throw new NotImplementedException();
        }

        public void PopulateChildren()
        {
            m_CachedChildren.Clear();

            foreach (var child in Node.Children)
            {
                if (!child.ShowForWorld(World))
                    continue;

                var item = Acquire(Graph, child, this, World);
                m_CachedChildren.Add(item);
            }
        }

        public void Release()
        {
            World = null;
            Graph = null;
            Node = null;
            parent = null;
            foreach (var child in m_CachedChildren.OfType<SystemTreeViewItem>())
            {
                child.Release();
            }

            m_CachedChildren.Clear();

            Pool.Release(this);
        }
    }
}
