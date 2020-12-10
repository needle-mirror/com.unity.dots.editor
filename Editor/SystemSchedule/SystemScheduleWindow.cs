using Unity.Properties;
using Unity.Properties.UI;
using Unity.Serialization.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class SystemScheduleWindow : DOTSEditorWindow, IHasCustomMenu
    {
        static readonly string k_WindowName = L10n.Tr("Systems");
        static readonly string k_ShowFullPlayerLoopString = L10n.Tr("Show Full Player Loop");
        static readonly string k_FilterComponentType = L10n.Tr("Component type");
        static readonly string k_FilterComponentTypeTooltip = L10n.Tr("Filter systems that have the specified component type in queries");
        static readonly string k_FilterSystemDependencies = L10n.Tr("System dependencies");
        static readonly string k_FilterSystemDependenciesTooltip = L10n.Tr("Filter systems by their direct dependencies");

        static readonly Vector2 k_MinWindowSize = new Vector2(200, 200);

        VisualElement m_Root;
        CenteredMessageElement m_NoWorld;
        SystemTreeView m_SystemTreeView;
        VisualElement m_WorldSelector;
        VisualElement m_EmptySelectorWhenShowingFullPlayerLoop;
        SearchElement m_SearchElement;

        /// <summary>
        /// Helper container to store session state data.
        /// </summary>
        class State
        {
            /// <summary>
            /// This field controls the showing of full player loop state.
            /// </summary>
            public bool ShowFullPlayerLoop;
        }

        /// <summary>
        /// State data for <see cref="SystemScheduleWindow"/>. This data is persisted between domain reloads.
        /// </summary>
        State m_State;

        [MenuItem(Constants.MenuItems.SystemScheduleWindow, false, Constants.MenuItems.WindowPriority)]
        static void OpenWindow()
        {
            var window = GetWindow<SystemScheduleWindow>();
            window.Show();
        }

        /// <summary>
        /// Build the GUI for the system window.
        /// </summary>
        void OnEnable()
        {
            titleContent = EditorGUIUtility.TrTextContent(k_WindowName, EditorIcons.System);
            minSize = k_MinWindowSize;

            m_Root = new VisualElement { style = { flexGrow = 1 } };
            rootVisualElement.Add(m_Root);

            m_NoWorld = new CenteredMessageElement() { Message = NoWorldMessageContent };
            rootVisualElement.Add(m_NoWorld);
            m_NoWorld.Hide();

            m_State = SessionState<State>.GetOrCreate($"{typeof(SystemScheduleWindow).FullName}+{nameof(State)}+{EditorWindowInstanceKey}");

            Resources.Templates.SystemSchedule.AddStyles(m_Root);
            Resources.Templates.DotsEditorCommon.AddStyles(m_Root);

            CreateToolBar(m_Root);
            CreateTreeViewHeader(m_Root);
            CreateTreeView(m_Root);

            PlayerLoopSystemGraph.Register();

            m_SearchElement.Search(SearchFilter);

            PlayerLoopSystemGraph.OnGraphChanged += RebuildTreeView;
            SystemDetailsVisualElement.OnAddFilter += OnAddFilter;
            SystemDetailsVisualElement.OnRemoveFilter += OnRemoveFilter;
        }

        protected override void OnWorldsChanged(bool containsAnyWorld)
        {
            m_Root.ToggleVisibility(containsAnyWorld);
            m_NoWorld.ToggleVisibility(!containsAnyWorld);
        }

        void OnDisable()
        {
            PlayerLoopSystemGraph.OnGraphChanged -= RebuildTreeView;
            SystemDetailsVisualElement.OnAddFilter -= OnAddFilter;
            SystemDetailsVisualElement.OnRemoveFilter -= OnRemoveFilter;

            PlayerLoopSystemGraph.Unregister();
            m_SystemTreeView.Dispose();
        }

        void CreateToolBar(VisualElement root)
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList(UssClasses.SystemScheduleWindow.ToolbarContainer);
            root.Add(toolbar);

            m_WorldSelector = CreateWorldSelector();
            toolbar.Add(m_WorldSelector);
            m_EmptySelectorWhenShowingFullPlayerLoop = new ToolbarMenu { text = k_ShowFullPlayerLoopString };
            toolbar.Add(m_EmptySelectorWhenShowingFullPlayerLoop);

            var rightSideContainer = new VisualElement();
            rightSideContainer.AddToClassList(UssClasses.SystemScheduleWindow.ToolbarRightSideContainer);

            AddSearchIcon(rightSideContainer, UssClasses.DotsEditorCommon.SearchIcon);
            AddSearchElement(root);

            var dropdownSettings = CreateDropdownSettings(UssClasses.DotsEditorCommon.SettingsIcon);
            dropdownSettings.menu.AppendAction(k_ShowFullPlayerLoopString, a =>
            {
                m_State.ShowFullPlayerLoop = !m_State.ShowFullPlayerLoop;

                UpdateWorldSelectorDisplay();

                if (World.All.Count > 0)
                    RebuildTreeView();
            }, a => m_State.ShowFullPlayerLoop ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            UpdateWorldSelectorDisplay();
            rightSideContainer.Add(dropdownSettings);
            toolbar.Add(rightSideContainer);
        }

        void AddSearchElement(VisualElement root)
        {
            m_SearchElement = AddSearchElement<SystemForSearch>(root, UssClasses.DotsEditorCommon.SearchFieldContainer);
            m_SearchElement.RegisterSearchQueryHandler<SystemForSearch>(query =>
            {
                var parseResult = SearchQueryParser.ParseSearchQuery(query);
                m_SystemTreeView.SetFilter(query, parseResult);
            });

            m_SearchElement.AddSearchFilterPopupItem(Constants.SystemSchedule.k_ComponentToken.Substring(0, 1), k_FilterComponentType, k_FilterComponentTypeTooltip);
            m_SearchElement.AddSearchFilterPopupItem(Constants.SystemSchedule.k_SystemDependencyToken.Substring(0, 2), k_FilterSystemDependencies, k_FilterSystemDependenciesTooltip);

            m_SearchElement.AddSearchDataProperty(new PropertyPath(nameof(SystemForSearch.SystemName)));
            m_SearchElement.AddSearchFilterProperty(Constants.SystemSchedule.k_ComponentToken.Substring(0, 1), new PropertyPath(nameof(SystemForSearch.ComponentNamesInQuery)));
            m_SearchElement.AddSearchFilterProperty(Constants.SystemSchedule.k_SystemDependencyToken.Substring(0, 2), new PropertyPath(nameof(SystemForSearch.SystemDependency)));
            m_SearchElement.EnableAutoComplete(ComponentTypeAutoComplete.Instance);
        }

        void UpdateWorldSelectorDisplay()
        {
            m_WorldSelector.ToggleVisibility(!m_State.ShowFullPlayerLoop);
            m_EmptySelectorWhenShowingFullPlayerLoop.ToggleVisibility(m_State.ShowFullPlayerLoop);
        }

        // Manually create header for the tree view.
        void CreateTreeViewHeader(VisualElement root)
        {
            var systemTreeViewHeader = new Toolbar();
            systemTreeViewHeader.AddToClassList(UssClasses.SystemScheduleWindow.TreeView.Header);

            var systemHeaderLabel = new Label("Systems");
            systemHeaderLabel.AddToClassList(UssClasses.SystemScheduleWindow.TreeView.System);

            var entityHeaderLabel = new Label("Matches")
            {
                tooltip = "The number of entities that match the queries at the end of the frame."
            };
            entityHeaderLabel.AddToClassList(UssClasses.SystemScheduleWindow.TreeView.Matches);

            var timeHeaderLabel = new Label("Time (ms)")
            {
                tooltip = "Average running time."
            };
            timeHeaderLabel.AddToClassList(UssClasses.SystemScheduleWindow.TreeView.Time);

            systemTreeViewHeader.Add(systemHeaderLabel);
            systemTreeViewHeader.Add(entityHeaderLabel);
            systemTreeViewHeader.Add(timeHeaderLabel);

            root.Add(systemTreeViewHeader);
        }

        void CreateTreeView(VisualElement root)
        {
            m_SystemTreeView = new SystemTreeView(EditorWindowInstanceKey)
            {
                style = { flexGrow = 1 }
            };
            root.Add(m_SystemTreeView);
        }

        void RebuildTreeView()
        {
            m_SystemTreeView.Refresh(m_State.ShowFullPlayerLoop ? null : SelectedWorld);
        }

        protected override void OnUpdate() { }

        protected override void OnWorldSelected(World world)
        {
            if (m_State.ShowFullPlayerLoop)
                return;

            RebuildTreeView();
        }

        void OnAddFilter(string toAdd)
        {
            AddStringToSearchField(toAdd);
        }

        void OnRemoveFilter(string toRemove)
        {
            RemoveStringFromSearchField(toRemove);
        }

        public void AddItemsToMenu(GenericMenu menu)
            => TelemetryWindow.AddCustomMenu(new SystemWindowTelemetryData(), menu);

        class SystemWindowTelemetryData : ITelemetry
        {
            string ITelemetry.Name { get; } = "Systems Window Telemetry";

            [CreateProperty] public int SystemTreeViewItemActiveInstanceCount => SystemTreeViewItem.Pool.ActiveInstanceCount;
            [CreateProperty] public int SystemTreeViewItemPoolSize => SystemTreeViewItem.Pool.PoolSize;
            [CreateProperty] public int SystemInformationVisualElementActiveInstanceCount => SystemInformationVisualElement.Pool.ActiveInstanceCount;
            [CreateProperty] public int SystemInformationVisualElementPoolSize => SystemInformationVisualElement.Pool.PoolSize;

            class Inspector : Inspector<SystemWindowTelemetryData>
            {
                public override VisualElement Build() => Resources.Templates.SystemWindowTelemetry.Clone();
            }
        }
    }
}
