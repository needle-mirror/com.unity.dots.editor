using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Serialization.Editor;

namespace Unity.Entities.Editor
{
    class SystemScheduleWindow : DOTSEditorWindow
    {
        static readonly string k_WindowName = L10n.Tr("Systems");
        static readonly string k_ShowFullPlayerLoopString = L10n.Tr("Show Full Player Loop");
        static readonly Vector2 k_MinWindowSize = new Vector2(200, 200);

        SystemScheduleTreeView m_SystemTreeView;
        VisualElement m_WorldSelector;
        VisualElement m_EmptySelectorWhenShowingFullPlayerLoop;
        int m_LastTimedFrame;

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

            m_State = SessionState<State>.GetOrCreate($"{typeof(SystemScheduleWindow).FullName}+{nameof(State)}+{EditorWindowInstanceKey}");

            Resources.Templates.SystemSchedule.AddStyles(rootVisualElement);
            Resources.Templates.DotsEditorCommon.AddStyles(rootVisualElement);

            CreateToolBar(rootVisualElement);
            CreateTreeViewHeader(rootVisualElement);
            CreateTreeView(rootVisualElement);

            if (World.All.Count > 0)
                BuildAll();

            PlayerLoopSystemGraph.OnGraphChanged += BuildAll;
            SystemDetailsVisualElement.OnAddFilter += OnAddFilter;
            SystemDetailsVisualElement.OnRemoveFilter += OnRemoveFilter;
        }

        void OnDisable()
        {
            PlayerLoopSystemGraph.OnGraphChanged -= BuildAll;
            SystemDetailsVisualElement.OnAddFilter -= OnAddFilter;
            SystemDetailsVisualElement.OnRemoveFilter -= OnRemoveFilter;
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
            AddSearchFieldContainer(root, UssClasses.DotsEditorCommon.SearchFieldContainer);

            var dropdownSettings = CreateDropdownSettings(UssClasses.DotsEditorCommon.SettingsIcon);
            dropdownSettings.menu.AppendAction(k_ShowFullPlayerLoopString, a =>
            {
                m_State.ShowFullPlayerLoop = !m_State.ShowFullPlayerLoop;

                UpdateWorldSelectorDisplay();

                if (World.All.Count > 0)
                    BuildAll();
            }, a => m_State.ShowFullPlayerLoop ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            UpdateWorldSelectorDisplay();
            rightSideContainer.Add(dropdownSettings);
            toolbar.Add(rightSideContainer);
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
            m_SystemTreeView = new SystemScheduleTreeView(EditorWindowInstanceKey);
            m_SystemTreeView.style.flexGrow = 1;
            m_SystemTreeView.SearchFilter = SearchFilter;
            root.Add(m_SystemTreeView);
        }

        void BuildAll()
        {
            m_SystemTreeView.Refresh(m_State.ShowFullPlayerLoop ? null : SelectedWorld);
        }

        protected override void OnUpdate()
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
            if (m_State.ShowFullPlayerLoop)
                return;

            BuildAll();
        }

        void OnAddFilter(string toAdd)
        {
            AddStringToSearchField(toAdd);
        }

        void OnRemoveFilter(string toRemove)
        {
            RemoveStringFromSearchField(toRemove);
        }

        protected override void OnFilterChanged(string filter)
        {
            m_SystemTreeView.SearchFilter = filter;
            BuildAll();
        }
    }
}
