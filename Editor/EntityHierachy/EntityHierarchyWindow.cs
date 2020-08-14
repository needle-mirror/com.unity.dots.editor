using System;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    interface IEntityHierarchy
    {
        IEntityHierarchyGroupingStrategy Strategy { get; }

        EntityQueryDesc QueryDesc { get; }

        World World { get; }
    }

    class EntityHierarchyWindow : DOTSEditorWindow, IEntityHierarchy
    {
        static readonly ProfilerMarker k_OnUpdateMarker = new ProfilerMarker($"{nameof(EntityHierarchyWindow)}.{nameof(OnUpdate)}");

        static readonly string k_WindowName = L10n.Tr("Entities");

        static readonly Vector2 k_MinWindowSize = new Vector2(200, 200); // Matches SceneHierarchy's min size

        readonly EntityHierarchyQueryBuilder m_EntityHierarchyQueryBuilder = new EntityHierarchyQueryBuilder();

        EntityHierarchy m_EntityHierarchy;
        EntityHierarchyDiffer m_EntityHierarchyDiffer;
        VisualElement m_EnableLiveLinkMessage;
        VisualElement m_Header;
        VisualElement m_Root;
        VisualElement m_NoWorld;

        [MenuItem(Constants.MenuItems.EntityHierarchyWindow, false, Constants.MenuItems.WindowPriority)]
        static void OpenWindow() => GetWindow<EntityHierarchyWindow>().Show();

        public IEntityHierarchyGroupingStrategy Strategy { get; private set; }

        public EntityQueryDesc QueryDesc { get; private set; }

        public World World { get; private set; }

        void OnEnable()
        {
            titleContent = new GUIContent(k_WindowName, EditorIcons.EntityGroup);
            minSize = k_MinWindowSize;

            m_Root = new VisualElement { style = { flexGrow = 1 } };
            rootVisualElement.Add(m_Root);

            m_NoWorld = CreateNoWorldMessage();
            rootVisualElement.Add(m_NoWorld);
            m_NoWorld.Hide();

            Resources.Templates.CommonResources.AddStyles(m_Root);
            Resources.Templates.DotsEditorCommon.AddStyles(m_Root);
            m_Root.AddToClassList(UssClasses.Resources.EntityHierarchy);

            m_EntityHierarchyQueryBuilder.Initialize();

            CreateToolbar();
            m_EntityHierarchy = new EntityHierarchy(new EntityHierarchyState(EditorWindowInstanceKey));
            m_Root.Add(m_EntityHierarchy);
            CreateEnableLiveLinkMessage();

            m_EntityHierarchy.Refresh(this);

            if (!string.IsNullOrEmpty(SearchFilter))
                OnFilterChanged(SearchFilter);

            LiveLinkConfigHelper.LiveLinkEnabledChanged += UpdateEnableLiveLinkMessage;
            EditorApplication.playModeStateChanged += UpdateEnableLiveLinkMessage;
        }

        protected override void OnWorldsChanged(bool containsAnyWorld)
        {
            m_Root.ToggleVisibility(containsAnyWorld);
            m_NoWorld.ToggleVisibility(!containsAnyWorld);
        }

        void OnDisable()
        {
            LiveLinkConfigHelper.LiveLinkEnabledChanged -= UpdateEnableLiveLinkMessage;
            EditorApplication.playModeStateChanged -= UpdateEnableLiveLinkMessage;
            m_EntityHierarchy.Dispose();
            if (Strategy != null)
            {
                m_EntityHierarchyDiffer?.Dispose();
                Strategy.Dispose();
            }
        }

        void UpdateEnableLiveLinkMessage()
        {
            m_EnableLiveLinkMessage.ToggleVisibility(!EditorApplication.isPlaying && !LiveLinkConfigHelper.LiveLinkEnabledInEditMode);
            m_EntityHierarchy.ToggleVisibility(EditorApplication.isPlaying || LiveLinkConfigHelper.LiveLinkEnabledInEditMode);
            m_Header.ToggleVisibility(EditorApplication.isPlaying || LiveLinkConfigHelper.LiveLinkEnabledInEditMode);
        }

        void UpdateEnableLiveLinkMessage(PlayModeStateChange _)
            => UpdateEnableLiveLinkMessage();

        void CreateToolbar()
        {
            m_Header = new VisualElement();
            Resources.Templates.EntityHierarchyToolbar.Clone(m_Header);
            var leftSide = m_Header.Q<VisualElement>(className: UssClasses.EntityHierarchyWindow.Toolbar.LeftSide);
            var rightSide = m_Header.Q<VisualElement>(className: UssClasses.EntityHierarchyWindow.Toolbar.RightSide);
            leftSide.Add(CreateWorldSelector());

            AddSearchIcon(rightSide, UssClasses.DotsEditorCommon.SearchIcon);
            AddSearchFieldContainer(m_Header, UssClasses.DotsEditorCommon.SearchFieldContainer);

            m_Root.Add(m_Header);
        }

        void CreateEnableLiveLinkMessage()
        {
            m_EnableLiveLinkMessage = new VisualElement { style = { flexGrow = 1 } };
            Resources.Templates.EntityHierarchyEnableLiveLinkMessage.Clone(m_EnableLiveLinkMessage);
            m_EnableLiveLinkMessage.Q<Button>().clicked += () => LiveLinkConfigHelper.LiveLinkEnabledInEditMode = true;
            m_Root.Add(m_EnableLiveLinkMessage);

            UpdateEnableLiveLinkMessage();
        }

        protected override void OnWorldSelected(World world)
        {
            if (world == World)
                return;

            if (Strategy != null)
            {
                m_EntityHierarchyDiffer?.Dispose();
                Strategy.Dispose();
                Strategy = null;
            }

            World = world;
            if (World != null)
            {
                Strategy = new EntityHierarchyDefaultGroupingStrategy(world);
                m_EntityHierarchyDiffer = new EntityHierarchyDiffer(this, 16);
                m_EntityHierarchy.Refresh(this);
            }
            else
            {
                m_EntityHierarchy.Clear();
            }
        }

        protected override void OnFilterChanged(string filter)
        {
            var result = m_EntityHierarchyQueryBuilder.BuildQuery(filter);
            QueryDesc = result.QueryDesc;
            m_EntityHierarchy.SetFilter(result);
        }

        protected override void OnUpdate()
        {
            using (k_OnUpdateMarker.Auto())
            {
                if (World == null || !m_EntityHierarchyDiffer.TryUpdate(out var structuralChangeDetected))
                    return;

                if (structuralChangeDetected)
                    m_EntityHierarchy.UpdateStructure();

                m_EntityHierarchy.OnUpdate();
            }
        }
    }
}
