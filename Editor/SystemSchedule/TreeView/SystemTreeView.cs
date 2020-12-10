using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Editor.Bridge;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine.UIElements;
using ListView = Unity.Editor.Bridge.ListView;

namespace Unity.Entities.Editor
{
    class SystemTreeView : VisualElement, IDisposable
    {
        static readonly string k_NoSystemsFoundTitle = L10n.Tr("No system matches your search");
        static readonly string k_ComponentTypeNotFoundTitle = L10n.Tr("Type not found");
        static readonly string k_ComponentTypeNotFoundContent = L10n.Tr("\"{0}\" is not a component type");

        readonly TreeView m_SystemTreeView;
        internal readonly IList<ITreeViewItem> m_TreeViewRootItems = new List<ITreeViewItem>();
        readonly ListView m_SystemListView; // For search results.
        internal readonly List<SystemTreeViewItem> m_ListViewFilteredItems = new List<SystemTreeViewItem>();

        SystemDetailsVisualElement m_SystemDetailsVisualElement;
        SystemTreeViewItem m_LastSelectedItem;
        int m_LastSelectedItemId;
        World m_World;
        CenteredMessageElement m_SearchEmptyMessage;
        int m_ScrollToItemId = -1;

        public SearchQueryParser.ParseResult SearchFilter;
        ISearchQuery<SystemForSearch> m_CurrentSearchQuery;

        List<SystemForSearch> m_AllSystemsForSearch = new List<SystemForSearch>();
        Dictionary<string, string[]> m_SystemDependencyMap = new Dictionary<string, string[]>();
        List<SystemForSearch> m_SearchResultsFlatSystemList = new List<SystemForSearch>();

        /// <summary>
        /// Constructor of the tree view.
        /// </summary>
        public SystemTreeView(string editorWindowInstanceKey)
        {
            m_SystemTreeView = new TreeView(m_TreeViewRootItems, Constants.ListView.ItemHeight, MakeTreeViewItem, ReleaseTreeViewItem, BindTreeViewItem)
            {
                viewDataKey = $"{Constants.State.ViewDataKeyPrefix}{typeof(SystemScheduleWindow).FullName}+{editorWindowInstanceKey}",
                selectionType = SelectionType.Single,
                name = "SystemTreeView",
                style = { flexGrow = 1 }
            };

            m_SystemTreeView.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (m_ScrollToItemId == -1)
                    return;

                var tempId = m_ScrollToItemId;
                m_ScrollToItemId = -1;
                if (m_SystemTreeView.FindItem(tempId) != null)
                    m_SystemTreeView.ScrollToItem(tempId);
            });

            m_SystemTreeView.onSelectionChange += OnSelectionChanged;
            Add(m_SystemTreeView);

            m_SystemDetailsVisualElement = new SystemDetailsVisualElement();

            m_SearchEmptyMessage = new CenteredMessageElement { Title = k_NoSystemsFoundTitle};
            m_SearchEmptyMessage.Hide();
            Add(m_SearchEmptyMessage);

            // Create list view for search results.
            m_SystemListView = new ListView(m_ListViewFilteredItems, Constants.ListView.ItemHeight, MakeListViewItem, ReleaseListViewItem, BindListViewItem)
            {
                selectionType = SelectionType.Single,
                name = "SystemListView",
                style = { flexGrow = 1 }
            };
            m_SystemListView.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == (int)MouseButton.LeftMouse)
                    Selection.activeObject = null;
            });

#if UNITY_2020_1_OR_NEWER
            m_SystemListView.onSelectionChange += OnSelectionChanged;
#else
            m_SystemListView.onSelectionChanged += OnSelectionChanged;
#endif

            Add(m_SystemListView);
        }

        void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (selection.FirstOrDefault() is SystemTreeViewItem selectedItem)
                OnSelectionChanged(selectedItem);
        }

        void OnSelectionChanged(SystemTreeViewItem selectedItem)
        {
            m_LastSelectedItem?.Release();
            m_LastSelectedItem = null;

            if (null == selectedItem)
                return;

            if (!selectedItem.SystemHandle.Valid)
            {
                if (Contains(m_SystemDetailsVisualElement))
                    Remove(m_SystemDetailsVisualElement);

                return;
            }

            // Remember last selected item id so that query information can be properly updated.
            m_LastSelectedItemId = selectedItem.id;

            m_LastSelectedItem = SystemTreeViewItem.Acquire(selectedItem.Graph, selectedItem.Node, (SystemTreeViewItem)selectedItem.parent, selectedItem.World);
            m_ScrollToItemId = selectedItem.id;

            // Start fresh.
            if (Contains(m_SystemDetailsVisualElement))
                Remove(m_SystemDetailsVisualElement);

            m_SystemDetailsVisualElement.Target = selectedItem;
            m_SystemDetailsVisualElement.SearchFilter = SearchFilter;
            m_SystemDetailsVisualElement.Parent = this;
            m_SystemDetailsVisualElement.LastSelectedItem = m_LastSelectedItem;
            Add(m_SystemDetailsVisualElement);
        }

        VisualElement MakeTreeViewItem() => SystemInformationVisualElement.Acquire(this, m_World);

        static void ReleaseTreeViewItem(VisualElement ve) => ((SystemInformationVisualElement)ve).Release();

        VisualElement MakeListViewItem()
        {
            // ListView changes user created VisualElements in a way that no reversible using public API
            // Wrapping pooled item in a non reusable container prevent us from reusing a pooled item in an eventual checked pseudo state
            var wrapper = new VisualElement();
            wrapper.Add(SystemInformationVisualElement.Acquire(this, m_World));
            return wrapper;
        }

        static void ReleaseListViewItem(VisualElement ve) => ((SystemInformationVisualElement)ve[0]).Release();

        public void SetFilter(ISearchQuery<SystemForSearch> searchQuery, SearchQueryParser.ParseResult parseResult)
        {
            m_CurrentSearchQuery = searchQuery;
            SearchFilter = parseResult;
            Refresh();
        }

        public void Refresh(World world)
        {
            if (m_World != world && Contains(m_SystemDetailsVisualElement))
                Remove(m_SystemDetailsVisualElement);

            m_World = world;

            m_AllSystemsForSearch.Clear();
            m_SystemDependencyMap.Clear();

            RecreateTreeViewRootItems();
            Refresh();
        }

        void RecreateTreeViewRootItems()
        {
            ReleaseAllPooledItems();

            if (World.All.Count > 0 && string.IsNullOrEmpty(SearchFilter.ErrorComponentType))
            {
                var graph = PlayerLoopSystemGraph.Current;

                foreach (var node in graph.Roots)
                {
                    if (!node.ShowForWorld(m_World))
                        continue;

                    var item = SystemTreeViewItem.Acquire(graph, node, null, m_World);
                    PopulateAllChildren(item);
                    m_TreeViewRootItems.Add(item);
                }

                m_SystemTreeView.Refresh();
            }
        }

        void PopulateAllChildren(SystemTreeViewItem item)
        {
            if (item.SystemHandle != null)
            {
                var systemForSearch = new SystemForSearch(item.SystemHandle) { Node = item.Node };
                m_AllSystemsForSearch.Add(systemForSearch);

                var keyString = Properties.Editor.TypeUtility.GetTypeDisplayName(item.SystemHandle.GetSystemType()).Replace(".", "|");

                // TODO: Find better solution to be able to uniquely identify each system.
                // At the moment, we are using system name to identify each system, which is not reliable
                // because there can be multiple systems with the same name in a world. This is only a
                // temporary solution to avoid the error of adding the same key into the map. We need to
                // find a proper solution to be able to uniquely identify each system.
                if (!m_SystemDependencyMap.ContainsKey(keyString))
                    m_SystemDependencyMap.Add(keyString, SystemDependencyUtilities.GetDependencySystemNamesFromGivenSystemType(item.SystemHandle.GetSystemType()).ToArray());
            }

            // Get last selected item.
            if (item.id == m_LastSelectedItemId)
            {
                m_LastSelectedItem?.Release();
                m_LastSelectedItem = SystemTreeViewItem.Acquire(item.Graph, item.Node, (SystemTreeViewItem)item.parent, item.World);
                m_SystemDetailsVisualElement.LastSelectedItem = m_LastSelectedItem;
            }

            if (!item.HasChildren)
                return;

            item.PopulateChildren();

            foreach (var child in item.children)
            {
                PopulateAllChildren(child as SystemTreeViewItem);
            }
        }

        void BuildFilterResults()
        {
            m_SearchResultsFlatSystemList.Clear();
            if (m_CurrentSearchQuery == null || string.IsNullOrWhiteSpace(m_CurrentSearchQuery.SearchString) || m_CurrentSearchQuery.Tokens.Count == 0 && string.IsNullOrEmpty(SearchFilter.ErrorComponentType))
            {
                m_SearchResultsFlatSystemList.AddRange(m_AllSystemsForSearch);
            }
            else
            {
                foreach (var systemForSearch in m_AllSystemsForSearch)
                {
                    systemForSearch.SystemDependencyCache = (from kvp in m_SystemDependencyMap where kvp.Value.Contains(systemForSearch.SystemName) select kvp.Key).ToArray();
                }

#if QUICKSEARCH_AVAILABLE
                m_SearchResultsFlatSystemList.AddRange( m_CurrentSearchQuery.Apply(m_AllSystemsForSearch));
#else
                using (var candidates = PooledHashSet<SystemForSearch>.Make())
                {
                    foreach (var system in m_AllSystemsForSearch)
                    {
                        if (SearchFilter.Names.All(n => system.SystemName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                            candidates.Set.Add(system);

                        if (candidates.Set.Contains(system) && !SearchFilter.ComponentNames.All(component => system.ComponentNamesInQuery.Any(c => c.IndexOf(component, StringComparison.OrdinalIgnoreCase) >= 0)))
                            candidates.Set.Remove(system);

                        if (candidates.Set.Contains(system) && SearchFilter.DependencySystemNames.All(dependency => system.SystemDependency.Any(c => c.IndexOf(dependency, StringComparison.OrdinalIgnoreCase) >= 0)))
                            m_SearchResultsFlatSystemList.Add(system);
                    }
                }
#endif
            }
        }

        void PopulateListViewWithSearchResults()
        {
            BuildFilterResults();

            foreach (var filteredItem in m_ListViewFilteredItems)
            {
                filteredItem.Release();
            }
            m_ListViewFilteredItems.Clear();
            foreach (var system in m_SearchResultsFlatSystemList)
            {
                var listViewItems = SystemTreeViewItem.Acquire(PlayerLoopSystemGraph.Current, system.Node, null, m_World);
                m_ListViewFilteredItems.Add(listViewItems);
            }

            m_SystemListView.Refresh();
        }

        /// <summary>
        /// Refresh tree view to update with latest information.
        /// </summary>
        void Refresh()
        {
            // System details need to be updated also.
            m_SystemDetailsVisualElement.Target = m_LastSelectedItem;
            m_SystemDetailsVisualElement.SearchFilter = SearchFilter;

            // Check if there is search result
            if (!SearchFilter.IsEmpty)
            {
                PopulateListViewWithSearchResults();
                var hasSearchResult = m_ListViewFilteredItems.Any();

                m_SystemListView.ToggleVisibility(hasSearchResult);
                m_SystemTreeView.Hide();

                m_SystemDetailsVisualElement.ToggleVisibility(hasSearchResult);
                m_SearchEmptyMessage.ToggleVisibility(!hasSearchResult);
                if (string.IsNullOrEmpty(SearchFilter.ErrorComponentType))
                {
                    m_SearchEmptyMessage.Title = k_NoSystemsFoundTitle;
                    m_SearchEmptyMessage.Message = string.Empty;
                }
                else
                {
                    m_SearchEmptyMessage.Title = k_ComponentTypeNotFoundTitle;
                    m_SearchEmptyMessage.Message = string.Format(k_ComponentTypeNotFoundContent, SearchFilter.ErrorComponentType);
                }

                // Remove detail section if not in the results.
                if (Contains(m_SystemDetailsVisualElement) && !m_ListViewFilteredItems.Contains(m_LastSelectedItem))
                    Remove(m_SystemDetailsVisualElement);
            }
            else
            {
                m_SystemListView.Hide();
                m_SystemTreeView.Show();

                m_SearchEmptyMessage.Hide();
                m_SystemDetailsVisualElement.Show();
            }

            SetSelection();
        }

        void SetSelection()
        {
            if (SearchFilter.IsEmpty) // Tree view
            {
                if (m_SystemListView.selectedItem is SystemTreeViewItem item)
                {
                    m_SystemTreeView.ClearSelection();
                    m_SystemTreeView.Select(item.id, false);
                }
            }
            else // List view
            {
                if (m_LastSelectedItem is SystemTreeViewItem lastSelectedItem)
                {
                    m_SystemListView.ClearSelection();
                    var index = m_ListViewFilteredItems.FindIndex(item => item.id == lastSelectedItem.id);
                    if (index != -1)
                    {
                        m_SystemListView.ScrollToItem(index);
                        m_SystemListView.selectedIndex = index;
                    }
                }
            }
        }

        void BindTreeViewItem(VisualElement element, ITreeViewItem item)
        {
            var target = item as SystemTreeViewItem;
            var systemInformationElement = element as SystemInformationVisualElement;
            if (null == systemInformationElement)
                return;

            systemInformationElement.Target = target;
            systemInformationElement.World = m_World;
            systemInformationElement.Update();
        }

        void BindListViewItem(VisualElement element, int itemIndex) => BindTreeViewItem(element[0], (ITreeViewItem)m_SystemListView.itemsSource[itemIndex]);

        public void Dispose() => ReleaseAllPooledItems();

        void ReleaseAllPooledItems()
        {
            foreach (var rootItem in m_TreeViewRootItems)
            {
                ((SystemTreeViewItem)rootItem).Release();
            }
            m_TreeViewRootItems.Clear();

            foreach (var filteredItem in m_ListViewFilteredItems)
            {
                filteredItem.Release();
            }
            m_ListViewFilteredItems.Clear();

            if (m_LastSelectedItem != null)
            {
                m_LastSelectedItem?.Release();
                m_LastSelectedItem = null;
            }

            m_SystemTreeView.Refresh();
            m_SystemListView.Refresh();
        }
    }
}
