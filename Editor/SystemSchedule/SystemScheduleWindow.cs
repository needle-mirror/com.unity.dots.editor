using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.LowLevel;

namespace Unity.Entities.Editor
{
    class SystemScheduleWindow : DOTSEditorWindow
    {
        static readonly string k_WindowName = L10n.Tr("Systems");
        const string k_ShowFullPlayerLoopString = "Show Full Player Loop";
        const string k_ShowInactiveSystemsString = "Show Inactive Systems";

        static World CurrentWorld { get; set; }
        static PlayerLoopSystem CurrentLoopSystem { get; set; }

        SystemScheduleTreeView m_SystemTreeView;
        ToolbarSearchField m_SearchField;
        ToolbarMenu m_WorldMenu;

        // To get information after domain reload.
        const string k_StateKey = nameof(SystemScheduleWindow) + "." + nameof(State);

        /// <summary>
        /// Helper container to store session state data.
        /// </summary>
        class State
        {
            /// <summary>
            /// This field controls the showing of full player loop state.
            /// </summary>
            public bool ShowFullPlayerLoop;

            /// <summary>
            /// This field controls the showing of inactive system state.
            /// </summary>
            public bool ShowInactiveSystems;
        }

        /// <summary>
        /// State data for <see cref="SystemScheduleWindow"/>. This data is persisted between domain reloads.
        /// </summary>
        State m_State;

        [MenuItem(Constants.MenuItems.SystemScheduleWindow, false)]
        static void OpenWindow()
        {
            var window = GetWindow<SystemScheduleWindow>();
            window.Show();
        }

        /// <summary>
        /// Build the GUI for the system window.
        /// </summary>
        public void OnEnable()
        {
            titleContent = EditorGUIUtility.TrTextContent(k_WindowName);
            minSize = new Vector2(600, 300);

            m_State = SessionState<State>.GetOrCreateState(k_StateKey);

            var root = rootVisualElement;
            Resources.Templates.SystemSchedule.AddStyles(root);

            // Create toolbar for world drop-down, search field.
            CreateToolBar(root);

            // Create a header for treeview.
            CreateTreeViewHeader(root);

            // Create tree view for systems.
            m_SystemTreeView = new SystemScheduleTreeView();
            m_SystemTreeView.style.flexGrow = 1;

            root.Add(m_SystemTreeView);

            if (World.All.Count > 0)
                BuildAll();

            PlayerLoopSystemGraph.OnGraphChanged += BuildAll;
        }

        void OnDisable()
        {
            PlayerLoopSystemGraph.OnGraphChanged -= BuildAll;
        }

        // Create toolbar, including World drop-down, toggles, search field.
        void CreateToolBar(VisualElement root)
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList(UssClasses.SystemScheduleWindow.ToolbarContainer);
            root.Add(toolbar);

            m_WorldMenu = CreateWorldSelector();
            toolbar.Add(m_WorldMenu);

            // right side container for styling.
            var rightSideContainer = new VisualElement();
            rightSideContainer.AddToClassList(UssClasses.SystemScheduleWindow.ToolbarRightSideContainer);

            AddSearchField(rightSideContainer, UssClasses.SystemScheduleWindow.SearchField);
            
            var dropDownSettings = CreateDropDownSettings(UssClasses.Common.SettingsIcon);
            UpdateDropDownSettings(dropDownSettings);
            rightSideContainer.Add(dropDownSettings);

            toolbar.Add(rightSideContainer);
        }
        
        void UpdateDropDownSettings(ToolbarMenu dropdownSettings)
        {
            var menu = dropdownSettings.menu;

            menu.AppendAction(k_ShowFullPlayerLoopString, a =>
            {
                m_State.ShowFullPlayerLoop = !m_State.ShowFullPlayerLoop;
                
                if (World.All.Count > 0)
                    BuildAll();
            }, a => m_State.ShowFullPlayerLoop ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);

            menu.AppendAction(k_ShowInactiveSystemsString, a =>
            {
                m_State.ShowInactiveSystems = !m_State.ShowInactiveSystems;
                if (World.All.Count > 0)
                    BuildAll();
            }, a => m_State.ShowInactiveSystems ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
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

        // Build the root node for the tree view.
        void BuildAll()
        {
            CurrentWorld = !m_State.ShowFullPlayerLoop ? GetCurrentlySelectedWorld() : null;
            m_SystemTreeView.Refresh(CurrentWorld, m_State.ShowInactiveSystems);
        }

        protected override void OnUpdate()
        {
            if (m_State.ShowFullPlayerLoop)
            {
                var menu = m_WorldMenu.menu;
                var menuItemsCount = menu.MenuItems().Count;

                for (var i = 0; i < menuItemsCount; i++)
                {
                    menu.RemoveItemAt(0);
                }
                
                m_WorldMenu.text = k_ShowFullPlayerLoopString;
            }
            
            if (GetCurrentlySelectedWorld() == null)
                return;

            UpdateTimings();
        }

        int m_LastTimedFrame;

        void UpdateTimings()
        {
            if (Time.frameCount == m_LastTimedFrame)
                return;

            var data = PlayerLoopSystemGraph.Current;
            foreach (var recorder in data.RecordersBySystem.Values)
            {
                recorder.Update();
            }

            m_LastTimedFrame = Time.frameCount;
        }

        protected override void OnWorldSelected(World world)
        {
            BuildAll();
        }

        protected override void OnFilterChanged(string filter)
        {
            m_SystemTreeView.SearchFilter = filter;
            BuildAll();
        }
    }
}
