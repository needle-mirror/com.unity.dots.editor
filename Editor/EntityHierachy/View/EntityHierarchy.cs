using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Editor.Bridge;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine.UIElements;
using ListView = Unity.Editor.Bridge.ListView;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities.Editor
{
    class EntityHierarchy : VisualElement, IDisposable
    {
        enum ViewMode { Uninitialized, Full, Search, Message }

        internal static readonly string ComponentTypeNotFoundTitle = L10n.Tr("Type not found");
        internal static readonly string ComponentTypeNotFoundContent = L10n.Tr("\"{0}\" is not a component type");
        internal static readonly string NoEntitiesFoundTitle = L10n.Tr("No entity matches your search");

        readonly int[] m_CachedSingleSelectionBuffer = new int[1];

        readonly List<ITreeViewItem> m_TreeViewRootItems = new List<ITreeViewItem>(128);
        readonly List<int> m_TreeViewItemsToExpand = new List<int>(128);
        readonly List<EntityHierarchyItem> m_ListViewFilteredItems = new List<EntityHierarchyItem>(1024);
        readonly EntityHierarchyFoldingState m_EntityHierarchyFoldingState;
        readonly VisualElement m_ViewContainer;
        readonly TreeView m_TreeView;
        readonly ListView m_ListView;
        readonly HierarchyItemsCache m_ItemsCache;
        readonly CenteredMessageElement m_SearchEmptyMessage;

        ViewMode m_CurrentViewMode;
        IEntityHierarchy m_Hierarchy;
        EntityHierarchyQueryBuilder.Result m_QueryBuilderResult;
        bool m_SearcherCacheNeedsRebuild = true;
        bool m_StructureChanged;
        uint m_RootVersion;
        bool m_QueryChanged;
        ISearchQuery<EntityHierarchyItem> m_CurrentQuery;
        EntityHierarchyNodeId m_SelectedItem;

        public EntityHierarchy(EntityHierarchyFoldingState entityHierarchyFoldingState)
        {
            m_EntityHierarchyFoldingState = entityHierarchyFoldingState;

            style.flexGrow = 1.0f;
            m_ViewContainer = new VisualElement();
            m_ViewContainer.style.flexGrow = 1.0f;
            m_ViewContainer.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == (int)MouseButton.LeftMouse)
                    Selection.activeObject = null;
            });
            m_TreeView = new TreeView(m_TreeViewRootItems, Constants.ListView.ItemHeight, MakeTreeViewItem, ReleaseTreeViewItem, BindTreeViewItem)
            {
                selectionType = SelectionType.Single,
                name = Constants.EntityHierarchy.FullViewName,
                style = { flexGrow = 1 },
            };
            m_TreeView.onSelectionChange += OnLocalSelectionChanged;
            m_TreeView.ItemExpandedStateChanging += (item, isExpanding) =>
            {
                var entityHierarchyItem = (EntityHierarchyItem)item;
                if (entityHierarchyItem.NodeId.Kind == NodeKind.Scene || entityHierarchyItem.NodeId.Kind == NodeKind.SubScene)
                    m_EntityHierarchyFoldingState.OnFoldingStateChanged(entityHierarchyItem.NodeId, isExpanding);
            };
            m_TreeView.Hide();
            m_ViewContainer.Add(m_TreeView);

            m_ListView = new ListView(m_ListViewFilteredItems, Constants.ListView.ItemHeight, MakeListViewItem, ReleaseListViewItem, BindListViewItem)
            {
                selectionType = SelectionType.Single,
                name = Constants.EntityHierarchy.SearchViewName,
                style = { flexGrow = 1 }
            };

            m_ListView.Hide();
            m_ViewContainer.Add(m_ListView);

            m_SearchEmptyMessage = new CenteredMessageElement();
            m_SearchEmptyMessage.Hide();
            Add(m_SearchEmptyMessage);

#if UNITY_2020_1_OR_NEWER
            m_ListView.onSelectionChange += OnLocalSelectionChanged;
#else
            m_ListView.onSelectionChanged += OnSelectionChanged;
#endif

            m_ItemsCache = new HierarchyItemsCache();

            m_CurrentViewMode = ViewMode.Full;

            Add(m_ViewContainer);
            Selection.selectionChanged += OnGlobalSelectionChanged;
        }

        public void Dispose()
        {
            // ReSharper disable once DelegateSubtraction
            Selection.selectionChanged -= OnGlobalSelectionChanged;

            Clear();
        }

        public void SetFilter(ISearchQuery<EntityHierarchyItem> searchQuery, EntityHierarchyQueryBuilder.Result queryBuilderResult)
        {
            m_QueryBuilderResult = queryBuilderResult;
            m_SearchEmptyMessage.ToggleVisibility(!queryBuilderResult.IsValid);
            m_ViewContainer.ToggleVisibility(queryBuilderResult.IsValid);

            if (!queryBuilderResult.IsValid)
            {
                m_SearchEmptyMessage.Title = ComponentTypeNotFoundTitle;
                m_SearchEmptyMessage.Message = string.Format(ComponentTypeNotFoundContent, queryBuilderResult.ErrorComponentType);
                m_CurrentViewMode = ViewMode.Message;
                return;
            }

            m_CurrentQuery = searchQuery;
            m_QueryChanged = true;
            var showFilterView = queryBuilderResult.QueryDesc != null || m_CurrentQuery != null && !string.IsNullOrWhiteSpace(m_CurrentQuery.SearchString) && m_CurrentQuery.Tokens.Count != 0;

            m_CurrentViewMode = showFilterView ? ViewMode.Search : ViewMode.Full;
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
            ClearTreeViewRootItems();
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
            if (m_Hierarchy?.GroupingStrategy == null)
                return;

            var rootVersion = m_Hierarchy.State.GetNodeVersion(EntityHierarchyNodeId.Root);
            if (m_StructureChanged || rootVersion != m_RootVersion)
            {
                m_QueryChanged = false;
                m_StructureChanged = false;
                m_RootVersion = rootVersion;

                RecreateRootItems();
                RecreateItemsToExpand();
                RefreshView();
            }
            else if (m_QueryChanged)
            {
                m_QueryChanged = false;
                RefreshView();
            }
        }

        void ClearTreeViewRootItems()
        {
            foreach (var child in m_TreeViewRootItems)
                ((EntityHierarchyItem)child).Release();

            m_TreeViewRootItems.Clear();
        }

        void RecreateRootItems()
        {
            ClearTreeViewRootItems();

            // We need to refresh the treeview since we changed its source collection
            // otherwise it could keep references to pooled objects that have been reset.
            m_TreeView.Refresh();

            if (m_Hierarchy?.GroupingStrategy == null)
                return;

            using (var rootNodes = m_Hierarchy.State.GetChildren(EntityHierarchyNodeId.Root, Allocator.TempJob))
            {
                foreach (var node in rootNodes)
                    m_TreeViewRootItems.Add(EntityHierarchyItem.Acquire(null, node, m_Hierarchy));
            }
        }

        void RecreateItemsToExpand()
        {
            m_TreeViewItemsToExpand.Clear();
            foreach (var treeViewRootItem in m_TreeViewRootItems)
            {
                var hierarchyItem = (EntityHierarchyItem)treeViewRootItem;
                if (hierarchyItem.NodeId.Kind != NodeKind.Scene || m_EntityHierarchyFoldingState.GetFoldingState(hierarchyItem.NodeId) == false)
                    continue;

                m_TreeViewItemsToExpand.Add(hierarchyItem.id);

                if (!hierarchyItem.hasChildren)
                    continue;

                foreach (var childItem in hierarchyItem.Children)
                {
                    if (childItem.NodeId.Kind != NodeKind.SubScene || m_EntityHierarchyFoldingState.GetFoldingState(childItem.NodeId) == false)
                        continue;

                    m_TreeViewItemsToExpand.Add(childItem.id);
                }
            }
        }

        void RefreshView()
        {
            // This is split in two because RefreshSearchView
            // can change the current view mode if no result is found.
            // We need to refresh the data before deciding what to show
            if (m_CurrentViewMode == ViewMode.Full)
            {
                m_TreeView.PrepareItemsToExpand(m_TreeViewItemsToExpand);
                m_TreeView.Refresh();
            }
            else if (m_CurrentViewMode == ViewMode.Search)
                RefreshSearchView();

            switch (m_CurrentViewMode)
            {
                case ViewMode.Full:
                    TrySelect(m_SelectedItem);

                    m_SearchEmptyMessage.Hide();
                    m_ListView.Hide();
                    m_TreeView.Show();
                    m_ViewContainer.Show();
                    break;
                case ViewMode.Search:
                    TrySelect(m_SelectedItem);

                    m_SearchEmptyMessage.Hide();
                    m_TreeView.Hide();
                    m_ListView.Show();
                    m_ViewContainer.Show();
                    break;
                case ViewMode.Message:
                    m_SearchEmptyMessage.Show();
                    m_TreeView.Hide();
                    m_ListView.Hide();
                    m_ViewContainer.Hide();
                    break;
            }
        }

        bool TrySelect(EntityHierarchyNodeId id)
        {
            if (id == default || !m_Hierarchy.State.Exists(id))
            {
                if (m_SelectedItem != default)
                    Deselect();

                return false;
            }

            Select(id);
            return true;
        }

        void Select(EntityHierarchyNodeId id)
        {
            m_SelectedItem = id;
            switch (m_CurrentViewMode)
            {
                case ViewMode.Full:
                {
                    m_TreeView.Select(id.GetHashCode(), false);
                    break;
                }
                case ViewMode.Search:
                {
                    var index = m_ListViewFilteredItems.FindIndex(item => item.NodeId == id);
                    if (index != -1)
                    {
                        m_ListView.ScrollToItem(index);
                        m_CachedSingleSelectionBuffer[0] = index;
                        m_ListView.SetSelectionWithoutNotify(m_CachedSingleSelectionBuffer);
                    }

                    break;
                }
            }
        }

        void Deselect()
        {
            m_SelectedItem = default;
            m_TreeView.ClearSelection();
            m_ListView.ClearSelection();
        }

        void RefreshSearchView()
        {
            if (m_SearcherCacheNeedsRebuild)
            {
                m_ItemsCache.Rebuild(m_TreeViewRootItems.OfType<EntityHierarchyItem>());
                m_SearcherCacheNeedsRebuild = false;
            }

            m_ListViewFilteredItems.Clear();
            var filteredData = m_CurrentQuery?.Apply(m_ItemsCache.Items) ?? m_ItemsCache.Items;
            EntityHierarchyItem lastSubsceneItem = null;
            foreach (var item in filteredData)
            {
                if (item.NodeId.Kind != NodeKind.Entity)
                    continue;

                if (item.parent != null && IsParentedBySubScene(item, out var closestSubScene) && closestSubScene != lastSubsceneItem)
                {
                    lastSubsceneItem = closestSubScene;
                    m_ListViewFilteredItems.Add(lastSubsceneItem);
                }

                m_ListViewFilteredItems.Add(item);
            }

            if (m_ListViewFilteredItems.Count == 0 && m_QueryBuilderResult.IsValid)
            {
                m_SearchEmptyMessage.Title = NoEntitiesFoundTitle;
                m_SearchEmptyMessage.Message = string.Empty;
                m_CurrentViewMode = ViewMode.Message;
            }

            m_ListView.Refresh();

            bool IsParentedBySubScene(EntityHierarchyItem item, out EntityHierarchyItem subSceneItem)
            {
                subSceneItem = null;

                var current = item;
                while (true)
                {
                    if (current.parent == null)
                        return false;

                    var currentParent = (EntityHierarchyItem) current.parent;
                    switch (currentParent.NodeId.Kind)
                    {
                        case NodeKind.Root:
                        case NodeKind.Scene:
                            return false;
                        case  NodeKind.Entity:
                            current = currentParent;
                            continue;
                        case NodeKind.SubScene:
                            subSceneItem = currentParent;
                            return true;
                        default:
                            throw new NotSupportedException($"{nameof(currentParent.NodeId.Kind)} is not supported in this context");
                    }
                }
            }
        }

        void OnLocalSelectionChanged(IEnumerable<object> selection)
        {
            if (selection.FirstOrDefault() is EntityHierarchyItem selectedItem)
                OnLocalSelectionChanged(selectedItem);
        }

        void OnLocalSelectionChanged(EntityHierarchyItem selectedItem)
        {
            m_SelectedItem = selectedItem.NodeId;
            if (selectedItem.NodeId.Kind == NodeKind.Entity)
            {
                var entity = selectedItem.NodeId.ToEntity();
                if (entity != Entity.Null)
                {
                    var undoGroup = Undo.GetCurrentGroup();
                    EntitySelectionProxy.SelectEntity(m_Hierarchy.World, entity);

                    // Collapsing the selection of the entity into the selection of the ListView / TreeView item
                    Undo.CollapseUndoOperations(undoGroup);
                }
            }
            else
            {
                // TODO: Deal with non-Entity selections
            }
        }

        void OnGlobalSelectionChanged()
        {
            if (Selection.activeObject is EntitySelectionProxy selectedProxy && selectedProxy.World == m_Hierarchy.World)
                TrySelect(EntityHierarchyNodeId.FromEntity(selectedProxy.Entity));
            else
                Deselect();
        }

        static VisualElement MakeTreeViewItem() => EntityHierarchyItemView.Acquire();

        static void ReleaseTreeViewItem(VisualElement ve) => ((EntityHierarchyItemView)ve).Release();

        static VisualElement MakeListViewItem()
        {
            // ListView changes user created VisualElements in a way that no reversible using public API
            // Wrapping pooled item in a non reusable container prevent us from reusing a pooled item in an eventual checked pseudo state
            var wrapper = new VisualElement();
            wrapper.Add(EntityHierarchyItemView.Acquire());
            return wrapper;
        }

        static void ReleaseListViewItem(VisualElement ve) => ((EntityHierarchyItemView)(ve[0])).Release();

        static void BindTreeViewItem(VisualElement element, ITreeViewItem item) => ((EntityHierarchyItemView)element).SetSource((EntityHierarchyItem)item);

        void BindListViewItem(VisualElement element, int itemIndex) => BindTreeViewItem(element[0], (ITreeViewItem)m_ListView.itemsSource[itemIndex]);
    }
}
