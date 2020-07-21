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

        static readonly string k_ComponentTypeNotFoundTitle = L10n.Tr("Type not found");
        static readonly string k_ComponentTypeNotFoundContent = L10n.Tr("No component type found matching \"{0}\"");
        static readonly string k_NoEntitiesFoundContent = L10n.Tr("No entities found");
        static readonly Regex k_ExtractionPattern = new Regex(@"\""(.+)\""|(\S+)", RegexOptions.Compiled | RegexOptions.Singleline);

        readonly List<ITreeViewItem> m_TreeViewRootItems = new List<ITreeViewItem>(128);
        readonly List<int> m_TreeViewItemsToExpand = new List<int>(128);
        readonly List<EntityHierarchyItem> m_ListViewFilteredItems = new List<EntityHierarchyItem>(1024);
        readonly List<string> m_Filters = new List<string>(8);
        readonly EntityHierarchyState m_EntityHierarchyState;
        readonly VisualElement m_ViewContainer;
        readonly TreeView m_TreeView;
        readonly ListView m_ListView;
        readonly VisualElement m_SearchEmptyMessage;
        readonly IHierarchySearcher m_Searcher;
        readonly EntitySelectionProxy m_SelectionProxy;

        ViewMode m_ViewMode = ViewMode.Uninitialized;
        IEntityHierarchy m_Hierarchy;
        string m_Filter;
        bool m_SearcherCacheNeedsRebuild = true;
        bool m_StructureChanged;
        uint m_RootVersion;

        public EntityHierarchy(EntityHierarchyState entityHierarchyState)
        {
            m_EntityHierarchyState = entityHierarchyState;

            style.flexGrow = 1.0f;
            m_ViewContainer = new VisualElement();
            m_ViewContainer.style.flexGrow = 1.0f;
            m_TreeView = new TreeView(m_TreeViewRootItems, Constants.ListView.ItemHeight, OnMakeItem, OnBindItem)
            {
                selectionType = SelectionType.Single,
                name = Constants.EntityHierarchy.FullViewName
            };
            m_TreeView.style.flexGrow = 1.0f;
            m_TreeView.onSelectionChange += OnSelectionChanged;
            m_TreeView.Q<ListView>().RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == (int)MouseButton.LeftMouse)
                    Selection.activeObject = null;
            });
            m_TreeView.ItemExpandedStateChanging += (item, isExpanding) =>
            {
                var entityHierarchyItem = (EntityHierarchyItem)item;
                if (entityHierarchyItem.NodeId.Kind == NodeKind.Scene || entityHierarchyItem.NodeId.Kind == NodeKind.SubScene)
                    m_EntityHierarchyState.OnFoldingStateChanged(entityHierarchyItem.NodeId, isExpanding);
            };

            m_ListView = new ListView(m_ListViewFilteredItems, Constants.ListView.ItemHeight, OnMakeItem, OnBindListItem)
            {
                selectionType = SelectionType.Single,
                name = Constants.EntityHierarchy.SearchViewName
            };
            m_ListView.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == (int) MouseButton.LeftMouse)
                    Selection.activeObject = null;
            });

            m_ListView.style.flexGrow = 1.0f;

            m_SearchEmptyMessage = new VisualElement { style = { flexGrow = 1 } };
            Resources.Templates.SearchEmptyMessage.Clone(m_SearchEmptyMessage);
            m_SearchEmptyMessage.ToggleVisibility(false);
            Add(m_SearchEmptyMessage);

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

            Add(m_ViewContainer);
            Selection.selectionChanged += GlobalSelectionChanged;
        }

        VisualElement CurrentView
        {
            get
            {
                switch (m_ViewMode)
                {
                    case ViewMode.Full:
                        return m_TreeView;
                    case ViewMode.Search:
                        return m_ListView;
                    default:
                        return null;
                }
            }
        }

        void GlobalSelectionChanged()
        {
            if (Selection.activeObject == m_SelectionProxy || ProxiesTargetSameEntity(Selection.activeObject as EntitySelectionProxy, m_SelectionProxy))
                return;

            m_ListView.ClearSelection();
            m_TreeView.ClearSelection();
        }

        static bool ProxiesTargetSameEntity(EntitySelectionProxy proxyA, EntitySelectionProxy proxyB)
        {
            return proxyA != null
                && proxyB != null
                && proxyA.EntityManager.World.IsCreated
                && proxyB.EntityManager.World.IsCreated
                && proxyA.World == proxyB.World
                && proxyA.Entity == proxyB.Entity;
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

        public void SetFilter(EntityHierarchyQueryBuilder.Result queryBuilderResult)
        {
            m_SearchEmptyMessage.ToggleVisibility(!queryBuilderResult.IsValid);
            m_ViewContainer.ToggleVisibility(queryBuilderResult.IsValid);

            if (!queryBuilderResult.IsValid)
            {
                UpdateSearchEmptyMessage(k_ComponentTypeNotFoundTitle, string.Format(k_ComponentTypeNotFoundContent, queryBuilderResult.ErrorComponentType));
                return;
            }

            var filter = string.IsNullOrWhiteSpace(queryBuilderResult.Filter) || string.IsNullOrEmpty(queryBuilderResult.Filter)
                ? null // Ensures that white space or string.Empty are not considered different than the default value for m_Filter
                : queryBuilderResult.Filter.ToLowerInvariant();

            m_Filter = filter;
            UpdateSearchFilters();
            var showFilterView = queryBuilderResult.QueryDesc != null || m_Filters.Count != 0;
            if (showFilterView && m_ViewMode != ViewMode.Search)
            {
                SwitchViewMode(ViewMode.Search);
                return;
            }

            if (!showFilterView && m_ViewMode == ViewMode.Search)
            {
                SwitchViewMode(ViewMode.Full);
                return;
            }

            RefreshView();
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
            RefreshView();
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
                if (hierarchyItem.NodeId.Kind != NodeKind.Scene || m_EntityHierarchyState.GetFoldingState(hierarchyItem.NodeId) == false)
                    continue;

                m_TreeViewItemsToExpand.Add(hierarchyItem.id);

                if (!hierarchyItem.hasChildren)
                    continue;

                foreach (var childItem in hierarchyItem.Children)
                {
                    if (childItem.NodeId.Kind != NodeKind.SubScene || m_EntityHierarchyState.GetFoldingState(childItem.NodeId) == false)
                        continue;

                    m_TreeViewItemsToExpand.Add(childItem.id);
                }
            }
        }

        void SwitchViewMode(ViewMode viewMode)
        {
            if (m_ViewMode == viewMode)
                return;

            var previousSelection = default(EntityHierarchyItem);

            if (CurrentView != null)
                m_ViewContainer.Remove(CurrentView);

            switch (viewMode)
            {
                case ViewMode.Full:
                {
                    if (m_ListView.selectedItem is EntityHierarchyItem item)
                        previousSelection = item;

                    m_TreeView.ClearSelection();
                    m_ViewContainer.Add(m_TreeView);
                    break;
                }
                case ViewMode.Search:
                {
                    if (m_TreeView.selectedItem is EntityHierarchyItem item)
                        previousSelection = item;

                    m_ListView.ClearSelection();
                    m_ViewContainer.Add(m_ListView);
                    break;
                }
                default:
                    throw new ArgumentException($"Cannot switch view mode to: {viewMode}");
            }

            m_ViewMode = viewMode;

            RefreshView();

            if (previousSelection != default)
                Select(previousSelection.id);
        }

        void RefreshView()
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

            if (m_ListViewFilteredItems.Count == 0)
                UpdateSearchEmptyMessage(string.Empty, k_NoEntitiesFoundContent);

            m_ViewContainer.ToggleVisibility(m_ListViewFilteredItems.Count > 0);
            m_SearchEmptyMessage.ToggleVisibility(m_ListViewFilteredItems.Count == 0);

            m_ListView.Refresh();
        }

        void UpdateSearchEmptyMessage(string title, string message)
        {
            var titleLabel = m_SearchEmptyMessage.Q<Label>(className: UssClasses.EntityHierarchyWindow.SearchEmptyMessage.Title);
            var messageLabel = m_SearchEmptyMessage.Q<Label>(className: UssClasses.EntityHierarchyWindow.SearchEmptyMessage.Message);
            titleLabel.ToggleVisibility(!string.IsNullOrEmpty(title));
            messageLabel.ToggleVisibility(!string.IsNullOrEmpty(message));
            titleLabel.text = title;
            messageLabel.text = message;
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
