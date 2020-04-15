using System.Linq;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using Image = UnityEngine.UIElements.Image;

namespace Unity.Entities.Editor
{
    class SystemDetails
    {
        const string k_QueriesTitle = "Queries";
        const string k_QueriesMatchTitle = "Matches";
        const string k_SchedulingTitle = "Scheduling";
        const string k_ShowDependencies = "Show Dependencies";

        public readonly VisualElement systemDetailContainer;

        public SystemDetails(SystemTreeViewItem target)
        {
            switch (target.System)
            {
                case null:
                case ComponentSystemGroup _:
                {
                    return;
                }
                default:
                    systemDetailContainer = new VisualElement();
                    break;
            }

            CreateToolBarForDetailSection(systemDetailContainer, target);

            Resources.Templates.CommonResources.AddStyles(systemDetailContainer);
            Resources.Templates.SystemScheduleDetailContent.Clone(systemDetailContainer);

            CreateQueryResultSection(systemDetailContainer, target);
            CreateScheduleFilterSection(systemDetailContainer);
        }

        static string GetDetailSystemClass(ComponentSystemBase system)
        {
            switch (system)
            {
                case null:
                    return "";
                case EntityCommandBufferSystem _:
                    return UssClasses.SystemScheduleWindow.Detail.CommandBufferIcon;
                case ComponentSystemBase _:
                    return UssClasses.SystemScheduleWindow.Detail.SystemIcon;
            }
        }

        void CreateToolBarForDetailSection(VisualElement rootContainer, SystemTreeViewItem target)
        {
            if (target == null)
                return;

            var systemDetailToolbar = new Toolbar();
            systemDetailToolbar.style.justifyContent = Justify.SpaceBetween;

            Resources.Templates.CommonResources.AddStyles(systemDetailToolbar);
            Resources.Templates.SystemScheduleDetailHeader.Clone(systemDetailToolbar);

            // Left side
            var icon = systemDetailToolbar.Q(className: UssClasses.SystemScheduleWindow.Detail.SystemIconName);
            icon.AddToClassList(GetDetailSystemClass(target?.System));

            var systemNameLabel = systemDetailToolbar.Q<Label>(className: UssClasses.SystemScheduleWindow.Detail.SystemNameLabel);
            var systemName = target.System.GetType().Name;
            systemNameLabel.text = systemName;

            // Right side
            var scriptFound = SearchForScript(systemName);
            if (scriptFound)
            {
                var scriptIcon = systemDetailToolbar.Q(className: UssClasses.SystemScheduleWindow.Detail.ScriptsIconName);
                scriptIcon.AddToClassList(UssClasses.SystemScheduleWindow.Detail.ScriptsIcon);
                scriptIcon.RegisterCallback<MouseUpEvent>(evt =>
                {
                    AssetDatabase.OpenAsset(scriptFound);
                });
            }

            var closeIcon = systemDetailToolbar.Q(className: UssClasses.SystemScheduleWindow.Detail.CloseIconName);
            closeIcon.AddToClassList(UssClasses.SystemScheduleWindow.Detail.CloseIcon);
            closeIcon.RegisterCallback<MouseUpEvent>(evt =>
            {
                rootContainer.Clear();
            });

            rootContainer.Add(systemDetailToolbar);
        }

        UnityEngine.Object SearchForScript(string systemName)
        {
            var assets = AssetDatabase.FindAssets(systemName + " t:Script");
            return assets.Select(asset => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(asset))).FirstOrDefault(a => a.name == systemName);
        }

        void CreateQueryResultSection(VisualElement systemDetailContainer, SystemTreeViewItem target)
        {
            if (target.System == null)
                return;

            var queryTitleLabel = systemDetailContainer.Q<Label>(className: UssClasses.SystemScheduleWindow.Detail.QueryTitleLabel);
            queryTitleLabel.text = k_QueriesTitle;

            var matchTitleLabel = systemDetailContainer.Q<Label>(className: UssClasses.SystemScheduleWindow.Detail.MatchTitleLabel);
            matchTitleLabel.text = k_QueriesMatchTitle;

            var allQueryResultContainer = systemDetailContainer.Q(className: UssClasses.SystemScheduleWindow.Detail.QueryRow2);

            // Query result for each row.
            foreach (var query in target.System.EntityQueries)
            {
                var eachRowContainer = new VisualElement();
                Resources.Templates.CommonResources.AddStyles(eachRowContainer);
                Resources.Templates.SystemScheduleDetailQuery.Clone(eachRowContainer);

                // Sort the components by their access mode, readonly, readwrite, etc.
                var queryTypeList = query.GetQueryTypes().ToList();
                queryTypeList.Sort(EntityQueryUtility.CompareTypes);

                // Icon container
                var queryIcon = eachRowContainer.Q(className: UssClasses.SystemScheduleWindow.Detail.QueryIconName);
                queryIcon.style.flexShrink = 1;
                queryIcon.AddToClassList(UssClasses.SystemScheduleWindow.Detail.QueryIcon);

                var allComponentContainer = eachRowContainer.Q(className: UssClasses.SystemScheduleWindow.Detail.AllComponentContainer);
                foreach (var queryType in queryTypeList)
                {
                    // Component toggle container.
                    var componentTypeNameToggleContainer = new VisualElement();
                    componentTypeNameToggleContainer.AddToClassList(UssClasses.SystemScheduleWindow.Detail.EachComponentContainer);

                    // Access mode.
                    var componentAccessModeIcon = new Image();
                    componentAccessModeIcon.AddToClassList(UssClasses.SystemScheduleWindow.Detail.ComponentAccessModeIcon);
                    componentAccessModeIcon.AddToClassList(EntityQueryUtility.StyleForAccessMode(queryType.AccessModeType));

                    // Component toggle.
                    var componentType = queryType.GetManagedType();
                    var componentTypeName = EntityQueryUtility.SpecifiedTypeName(componentType);
                    var componentTypeNameToggle = new CustomToolbarToggle(componentTypeName, componentAccessModeIcon);
                    componentTypeNameToggleContainer.Add(componentTypeNameToggle);

                    allComponentContainer.Add(componentTypeNameToggleContainer);
                }

                // Entity match label
                var matchCountContainer = eachRowContainer.Q(className: UssClasses.SystemScheduleWindow.Detail.EntityMatchCountContainer);
                var matchCountLabel = new EntityMatchCountVisualElement { Query = query };
                matchCountContainer.Add(matchCountLabel);

                allQueryResultContainer.Add(eachRowContainer);
            }
        }

        void CreateScheduleFilterSection(VisualElement systemDetailContainer)
        {
            var schedulingTitle = systemDetailContainer.Q<Label>(className: UssClasses.SystemScheduleWindow.Detail.SchedulingTitle);
            schedulingTitle.text = k_SchedulingTitle;

            var schedulingToggle = systemDetailContainer.Q<ToolbarToggle>(className: UssClasses.SystemScheduleWindow.Detail.SchedulingToggle);
            schedulingToggle.text = k_ShowDependencies;
        }
    }
}
