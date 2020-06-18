using System;
using System.Collections.Generic;

namespace Unity.Entities.Editor
{
    class DefaultHierarchySearcher : IHierarchySearcher
    {
        List<EntityHierarchyItem> m_CachedItems = new List<EntityHierarchyItem>(1024);
        IEnumerable<EntityHierarchyItem> m_RootItems;

        public bool IsDirty { get; private set;  }

        public void Dispose()
        {
            m_CachedItems = null;
            m_RootItems = null;
        }

        public void UpdateRoots(IEnumerable<EntityHierarchyItem> rootItems)
        {
            m_RootItems = rootItems;
            IsDirty = true;
        }

        public void Rebuild()
        {
            m_CachedItems.Clear();
            AppendAllItemsToCacheRecursively(m_RootItems);
            IsDirty = false;
        }

        // Note: Assumes that patterns only contains lowercase entries
        public void Search(List<string> patterns, List<EntityHierarchyItem> results)
        {
            if (IsDirty)
                Rebuild();

            var pendingAddition = (EntityHierarchyItem)null;

            foreach (var item in m_CachedItems)
            {
                var nodeId = item.NodeId;

                // Only keep the *closest* sub scenes to found entities
                // Note: We are only interested in SubScenes because, today, there are no ways of having a normal scene owning entities.
                if (nodeId.Kind == NodeKind.SubScene)
                {
                    pendingAddition = item;
                    continue;
                }

                // Discard all nodes that are neither sub scenes or entities
                if (nodeId.Kind != NodeKind.Entity)
                    continue;

                var lowerCaseName = item.GetCachedLowerCaseName();
                var match = true;
                for (var i = 0; match && i < patterns.Count; ++i)
                {
                    match &= lowerCaseName.IndexOf(patterns[i], StringComparison.Ordinal) != -1;
                }

                if (!match)
                    continue;

                // Don't show scene separators for nodes at the root
                if (item.parent == null)
                    pendingAddition = null;

                if (pendingAddition != null)
                {
                    results.Add(pendingAddition);
                    pendingAddition = null;
                }

                results.Add(item);
            }
        }

        void AppendAllItemsToCacheRecursively(IEnumerable<EntityHierarchyItem> itemsToAdd)
        {
            foreach (var item in itemsToAdd)
            {
                m_CachedItems.Add(item);

                // Forces the item to cache its lower case name
                // We want to do it in the prepare step because we can run it independently from the actual search
                item.GetCachedLowerCaseName();

                if (item.hasChildren)
                    AppendAllItemsToCacheRecursively(item.Children);
            }
        }
    }
}
