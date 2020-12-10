using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Editor.Bridge;
using UnityEditor;

namespace Unity.Entities.Editor
{
    class EntityHierarchyItem : ITreeViewItem
    {
        static readonly string k_ChildrenListModificationExceptionMessage =
            L10n.Tr($"{nameof(EntityHierarchyItem)} does not allow external modifications to its list of children.");

        internal static readonly BasicPool<EntityHierarchyItem> Pool = new BasicPool<EntityHierarchyItem>(() => new EntityHierarchyItem());

        readonly List<EntityHierarchyItem> m_Children = new List<EntityHierarchyItem>();
        bool m_ChildrenInitialized;

        // Caching name and pre-lowercased name to speed-up search
        string m_CachedName;

        // Public for binding with SearchElement
        // ReSharper disable once InconsistentNaming
        public string m_CachedLowerCaseName;

        IEntityHierarchy m_EntityHierarchy;

        EntityHierarchyItem() { }

        public static EntityHierarchyItem Acquire(ITreeViewItem parentItem, in EntityHierarchyNodeId nodeId, IEntityHierarchy entityHierarchy)
        {
            var item = Pool.Acquire();
            item.parent = parentItem;
            item.NodeId = nodeId;
            item.m_EntityHierarchy = entityHierarchy;

            return item;
        }

        public EntityHierarchyNodeId NodeId { get; private set; }

        public IEntityHierarchyState HierarchyState => m_EntityHierarchy.State;

        public World World => m_EntityHierarchy.World;

        public List<EntityHierarchyItem> Children
        {
            get
            {
                if (!m_ChildrenInitialized)
                {
                    PopulateChildren();
                    m_ChildrenInitialized = true;
                }
                return m_Children;
            }
        }

        public string CachedName => m_CachedName ?? (m_CachedName = HierarchyState.GetNodeName(NodeId));

        public void PrepareSearcheableName()
        {
            if (m_CachedLowerCaseName == null)
                m_CachedLowerCaseName = CachedName.ToLowerInvariant();
        }

        public int id => NodeId.GetHashCode();

        public ITreeViewItem parent { get; private set; }

        IEnumerable<ITreeViewItem> ITreeViewItem.children => Children;

        public bool hasChildren => HierarchyState.HasChildren(NodeId);

        void ITreeViewItem.AddChild(ITreeViewItem _) => throw new NotSupportedException(k_ChildrenListModificationExceptionMessage);

        void ITreeViewItem.AddChildren(IList<ITreeViewItem> _) => throw new NotSupportedException(k_ChildrenListModificationExceptionMessage);

        void ITreeViewItem.RemoveChild(ITreeViewItem _) => throw new NotSupportedException(k_ChildrenListModificationExceptionMessage);

        public void Release()
        {
            NodeId = default;
            m_CachedName = null;
            m_CachedLowerCaseName = null;
            m_EntityHierarchy = null;
            parent = null;
            m_ChildrenInitialized = false;

            foreach (var child in m_Children)
            {
                child.Release();
            }

            m_Children.Clear();
            Pool.Release(this);
        }

        void PopulateChildren()
        {
            using (var childNodes = HierarchyState.GetChildren(NodeId, Allocator.TempJob))
            {
                foreach (var node in childNodes)
                {
                    m_Children.Add(Acquire(this, node, m_EntityHierarchy));
                }
            }
        }
    }
}
