using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Editor.Bridge;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities.Editor
{
    class EntityHierarchy : VisualElement, IDisposable
    {
        enum ViewMode { Uninitialized, Full, Search }

        static readonly Regex k_ExtractionPattern = new Regex(@"\""(.+)\""|(\S+)", RegexOptions.Compiled | RegexOptions.Singleline);

        readonly List<ITreeViewItem> m_TreeViewRootItems = new List<ITreeViewItem>(128);
        readonly List<int> m_TreeViewItemsToExpand = new List<int>(128);
        readonly TreeView m_TreeView;

        readonly List<EntityHierarchyItem> m_ListViewFilteredItems = new List<EntityHierarchyItem>(1024);
        readonly ListView m_ListView;

        readonly IHierarchySearcher m_Searcher;

        VisualElement m_CurrentView;
        ViewMode m_ViewMode = ViewMode.Uninitialized;

        readonly EntitySelectionProxy m_SelectionProxy;

        IEntityHierarchy m_Hierarchy;
        string m_Filter;
        readonly List<string> m_Filters = new List<string>(8);
        bool m_SearcherCacheNeedsRebuild = true;
        bool m_StructureChanged;
        uint m_RootVersion;

        public EntityHierarchy()
        {
            style.flexGrow = 1.0f;

            m_TreeView = new TreeView(m_TreeViewRootItems, Constants.EntityHierarchy.ItemHeight, OnMakeItem, OnBindItem)
            {
                selectionType = SelectionType.Single,
                name = Constants.EntityHierarchy.FullViewName
            };
            m_TreeView.style.flexGrow = 1.0f;
            m_TreeView.onSelectionChange += OnSelectionChanged;
            m_TreeView.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == (int)MouseButton.LeftMouse)
                    m_TreeView.ClearSelection();
            });
            m_TreeView.ItemExpandedStateChanging += (item, isExpanding) =>
            {
                var entityHierarchyItem = (EntityHierarchyItem)item;
                if (entityHierarchyItem.NodeId.Kind == NodeKind.Scene || entityHierarchyItem.NodeId.Kind == NodeKind.SubScene)
                    EntityHierarchyState.OnFoldingStateChanged(entityHierarchyItem.NodeId, isExpanding);
            };

            m_ListView = new ListView(m_ListViewFilteredItems, Constants.EntityHierarchy.ItemHeight, OnMakeItem, OnBindListItem)
            {
                selectionType = SelectionType.Single,
                name = Constants.EntityHierarchy.SearchViewName
            };
            m_ListView.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == (int)MouseButton.LeftMouse)
                    m_ListView.ClearSelection();
            });

            m_ListView.style.flexGrow = 1.0f;

#if UNITY_2020_1_OR_NEWER
            m_ListView.onSelectionChange += OnSelectionChanged;
#else
            m_ListView.onSelectionChanged += OnSelectionChanged;
#endif

            m_SelectionProxy = ScriptableObject.CreateInstance<EntitySelectionProxy>();
            m_SelectionProxy.hideFlags = HideFlags.HideAndDontSave;
            m_SelectionProxy.EntityControlSelectButton += OnSelectionChangedByInspector;

            m_Searcher = new DefaultHierarchySearcher();

            SwitchViewMode(ViewMode.Full);
        }

        public void Dispose()
        {
            if (m_SelectionProxy != null)
                UnityObject.DestroyImmediate(m_SelectionProxy);

            m_Searcher.Dispose();
        }

        public void Select(int id)
        {
            switch (m_ViewMode)
            {
                case ViewMode.Full:
                {
                    m_TreeView.Select(id);
                    break;
                }
                case ViewMode.Search:
                {
                    var index = m_ListViewFilteredItems.FindIndex(item => item.NodeId.GetHashCode() == id);
                    if (index != -1)
                    {
                        m_ListView.ScrollToItem(index);
                        m_ListView.selectedIndex = index;
                    }

                    break;
                }
            }
        }

        public void SetFilter(string filter)
        {
            filter = string.IsNullOrWhiteSpace(filter) || string.IsNullOrEmpty(filter)
                ? null // Ensures that white space or string.Empty are not considered different than the default value for m_Filter
                : filter.ToLowerInvariant();

            if (m_Filter == filter)
                return;

            var previousFilter = m_Filter;
            m_Filter = filter;

            UpdateSearchFilters();

            if (string.IsNullOrEmpty(previousFilter))
                SwitchViewMode(ViewMode.Search);
            else if (string.IsNullOrEmpty(filter))
                SwitchViewMode(ViewMode.Full);
            else
                RefreshViewMode();
        }

        public void Refresh(IEntityHierarchy entityHierarchy)
        {
            if (m_Hierarchy == entityHierarchy)
                return;

            m_Hierarchy = entityHierarchy;
            UpdateStructure();
            OnUpdate();
        }

        public new void Clear()
        {
            m_TreeViewRootItems.Clear();
            m_ListViewFilteredItems.Clear();
            m_ListView.Refresh();
            m_TreeView.Refresh();
        }

        public void UpdateStructure()
        {
            // Topology changes will be applied during the next update
            m_StructureChanged = true;
            m_SearcherCacheNeedsRebuild = true;
            m_RootVersion = 0;
        }

        public void OnUpdate()
        {
            if (m_Hierarchy?.Strategy == null)
                return;

            var rootVersion = m_Hierarchy.Strategy.GetNodeVersion(EntityHierarchyNodeId.Root);
            if (!m_StructureChanged && rootVersion == m_RootVersion)
                return;

            m_StructureChanged = false;
            m_RootVersion = rootVersion;

            RecreateRootItems();
            RecreateItemsToExpand();
            RefreshViewMode();
        }

        void RecreateRootItems()
        {
            foreach (var child in m_TreeViewRootItems)
                ((IPoolable)child).ReturnToPool();

            m_TreeViewRootItems.Clear();

            EntityHierarchyPool.ReturnAllVisualElements(this);

            if (m_Hierarchy?.Strategy == null)
                return;

            using (var rootNodes = m_Hierarchy.Strategy.GetChildren(EntityHierarchyNodeId.Root, Allocator.TempJob))
            {
                foreach (var node in rootNodes)
                    m_TreeViewRootItems.Add(EntityHierarchyPool.GetTreeViewItem(null, node, m_Hierarchy));
            }
        }

        void RecreateItemsToExpand()
        {
            m_TreeViewItemsToExpand.Clear();
            foreach (var treeViewRootItem in m_TreeViewRootItems)
            {
                var hierarchyItem = (EntityHierarchyItem)treeViewRootItem;
                if (hierarchyItem.NodeId.Kind != NodeKind.Scene || EntityHierarchyState.GetFoldingState(hierarchyItem.NodeId) == false)
                    continue;

                m_TreeViewItemsToExpand.Add(hierarchyItem.id);

                if (!hierarchyItem.hasChildren)
                    continue;

                foreach (var childItem in hierarchyItem.Children)
                {
                    if (childItem.NodeId.Kind != NodeKind.SubScene || EntityHierarchyState.GetFoldingState(childItem.NodeId) == false)
                        continue;

                    m_TreeViewItemsToExpand.Add(childItem.id);
                }
            }
        }

        void SwitchViewMode(ViewMode viewMode)
        {
            if (m_ViewMode == viewMode)
                return;

            if (m_CurrentView != null)
                Remove(m_CurrentView);

            var previousSelection = default(EntityHierarchyItem);

            switch (viewMode)
            {
                case ViewMode.Full:
                {
                    if (m_ListView.selectedItem is EntityHierarchyItem item)
                        previousSelection = item;

                    m_TreeView.ClearSelection();
                    m_CurrentView = m_TreeView;
                    break;
                }
                case ViewMode.Search:
                {
                    if (m_TreeView.selectedItem is EntityHierarchyItem item)
                        previousSelection = item;

                    m_ListView.ClearSelection();
                    m_CurrentView = m_ListView;
                    break;
                }
                case ViewMode.Uninitialized:
                default:
                    throw new ArgumentException($"Cannot switch view mode to: {viewMode}");
            }

            Add(m_CurrentView);
            m_ViewMode = viewMode;

            RefreshViewMode();

            if (previousSelection != default)
                Select(previousSelection.id);
        }

        void RefreshViewMode()
        {
            switch (m_ViewMode)
            {
                case ViewMode.Full:
                {
                    m_TreeView.PrepareItemsToExpand(m_TreeViewItemsToExpand);
                    m_TreeView.Refresh();
                    break;
                }
                case ViewMode.Search:
                {
                    RefreshSearchView();
                    break;
                }
            }
        }

        void RefreshSearchView()
        {
            if (m_SearcherCacheNeedsRebuild)
            {
                m_Searcher.UpdateRoots(m_TreeViewRootItems.OfType<EntityHierarchyItem>());
                if (m_Searcher.IsDirty)
                    m_Searcher.Rebuild();

                m_SearcherCacheNeedsRebuild = false;
            }

            m_ListViewFilteredItems.Clear();
            m_Searcher.Search(m_Filters, m_ListViewFilteredItems);

            m_ListView.Refresh();
        }

        void UpdateSearchFilters()
        {
            m_Filters.Clear();

            if (string.IsNullOrEmpty(m_Filter))
                return;

            // Extract individual filters from search string
            var matches = k_ExtractionPattern.Matches(m_Filter);
            for (int i = 0; i < matches.Count; ++i)
            {
                var match = matches[i];
                m_Filters.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
            }

            if (m_Filters.Count <= 1)
                return;

            // Sort filters by length (longest first)
            // We are doing this because filters are additive and therefore longer filters are less likely to match making it faster to discard candidates
            m_Filters.Sort(
                (lhs, rhs) =>
                {
                    if (lhs.Length == rhs.Length)
                        return 0;
                    return lhs.Length < rhs.Length ? 1 : -1;
                });

            // Factor out repeats by removing short strings contained in longer strings
            // e.g.: [GameObject, Object, a, z] -> [GameObject, z]
            for (int i = m_Filters.Count - 1; i >= 0; --i)
            {
                var testString = m_Filters[i];
                var match = false;
                for (int j = 0; !match && j < i; ++j)
                {
                    match |= m_Filters[j].IndexOf(testString, StringComparison.Ordinal) != -1;
                }

                if (match)
                {
                    // Swap with last and remove
                    m_Filters[i] = m_Filters[m_Filters.Count - 1];
                    m_Filters.RemoveAt(m_Filters.Count - 1);
                }
            }
        }

        void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (selection.FirstOrDefault() is EntityHierarchyItem selectedItem)
                OnSelectionChanged(selectedItem);
        }

        void OnSelectionChanged(EntityHierarchyItem selectedItem)
        {
            // TODO: Support undo/redo (see: Hierarchy window)

            if (selectedItem.NodeId.Kind == NodeKind.Entity)
            {
                var entity = selectedItem.Strategy.GetUnderlyingEntity(selectedItem.NodeId);
                if (selectedItem.Strategy.GetUnderlyingEntity(selectedItem.NodeId) != Entity.Null)
                {
                    m_SelectionProxy.SetEntity(m_Hierarchy.World, entity);
                    Selection.activeObject = m_SelectionProxy;
                }
            }
            else
            {
                // TODO: Deal with non-Entity selections
                Selection.activeObject = null;
            }
        }

        void OnSelectionChangedByInspector(World world, Entity entity)
        {
            if (world != m_Hierarchy.World)
                return;

            var nodeId = EntityHierarchyNodeId.FromEntity(entity);
            if (!m_Hierarchy.Strategy.Exists(nodeId))
                return;

            Select(nodeId.GetHashCode());
        }

        VisualElement OnMakeItem() => EntityHierarchyPool.GetVisualElement(this);

        void OnBindItem(VisualElement element, ITreeViewItem item) => ((EntityHierarchyItemView)element).SetSource((EntityHierarchyItem)item);

        void OnBindListItem(VisualElement element, int itemIndex) => OnBindItem(element, (ITreeViewItem)m_ListView.itemsSource[itemIndex]);
    }
}
