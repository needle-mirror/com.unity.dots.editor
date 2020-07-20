namespace Unity.Entities.Editor
{
    static class UssClasses
    {
        public static class Resources
        {
            public const string SystemSchedule = "system-schedule__resources";
            public const string EntityHierarchy = "entity-hierarchy__resources";
        }

        public static class DotsEditorCommon
        {
            public const string CommonResources = "common-resources";
            public const string SettingsIcon = "settings-icon";
            public const string SearchIconContainer = "search-icon-container";
            public const string SearchIcon = "search-icon";

            public const string SearchFieldContainer = "search-field-container";
            public const string SearchField = "search-field";
            public const string SearchFieldCancelButton = SearchField + "__cancel-button";

            public const string CustomToolbarToggle = "toolbar-toggle";
            public const string CustomToolbarToggleLabelParent = CustomToolbarToggle + "__label-parent";
            public const string CustomToolbarToggleLabel = CustomToolbarToggle + "__label-with-icon";
            public const string CustomToolbarToggleOnlyLabel = CustomToolbarToggle + "__label-without-icon";
            public const string CustomToolbarToggleIcon = CustomToolbarToggle + "__icon";

            public const string CustomLabelUnderline = "label-with-underline";
        }

        public static class SystemScheduleWindow
        {
            public const string SystemSchedule = "system-schedule";
            public const string ToolbarContainer = SystemSchedule + "__toolbar-container";
            public const string ToolbarRightSideContainer = SystemSchedule + "__toolbar-right-side-container";

            public static class TreeView
            {
                public const string Header = SystemSchedule + "__tree-view__header";
                public const string System = SystemSchedule + "__tree-view__system-label";
                public const string Matches = SystemSchedule + "__tree-view__matches-label";
                public const string Time = SystemSchedule + "__tree-view__time-label";
            }

            public static class Items
            {
                const string Base = SystemSchedule + "-item";
                public const string Icon = Base + "__icon";
                public const string Enabled = Base + "__enabled-toggle";
                public const string SystemName = Base + "__name-label";
                public const string Matches = Base + "__matches-label";
                public const string Time = Base + "__time-label";

                public const string SystemIcon = Icon + "--system";
                public const string SystemGroupIcon = Icon + "--system-group";
                public const string CommandBufferIcon = Icon + "--command-buffer";

                public const string SystemNameNormal = Base + "__name-label-normal";
                public const string SystemNameBold = Base + "__name-label-bold";
            }

            public static class Detail
            {
                const string Base = SystemSchedule + "-detail";
                const string Header = Base + "__header";
                const string Icon = Base + "__icon";

                public const string Content = Base + "__content";
                public const string SystemIcon = Icon + "--system";
                public const string GroupIcon = Icon + "--group";
                public const string CommandBufferIcon = Icon + "--command-buffer";
                public const string ScriptsIcon = Icon + "--scripts";
                public const string QueryIcon = Icon + "--query";
                public const string ReadOnlyIcon = Icon + "--read-only";
                public const string ReadWriteIcon = Icon + "--read-write";
                public const string WriteOnlyIcon = Icon + "--write-only";
                public const string ExcludeIcon = Icon + "--exclude";

                public const string SystemIconName = Header + "-system-icon";
                public const string ScriptsIconName = Header + "-scripts-icon";
                public const string SystemNameLabel = Header + "-system-name-label";
                public const string MiddleSection = Header + "-middle-section";
                public const string ResizeBar = Header + "-resize-bar";

                public const string QueryTitleLabel = Content + "-query-title-label";
                public const string MatchTitleLabel = Content + "-match-title-label";
                public const string QueryRow2 = Content + "-query-row-2";
                public const string QueryIconName = Content + "-query-icon";
                public const string AllComponentContainer = Content + "-all-component-container";
                public const string EachComponentContainer = Content + "-each-component-container";
                public const string ComponentAccessModeIcon = Content + "-component-access-icon";
                public const string EntityMatchCountContainer = Content + "-match-container";
                public const string ShowMoreLessLabel = Content + "-show-more-less-label";

                public const string SchedulingTitle = Content + "-scheduling-title";
                public const string SchedulingToggle = Content + "-scheduling-toggle";
            }
        }

        public static class EntityHierarchyWindow
        {
            const string k_EntityHierarchyBase = "entity-hierarchy";

            public static class Toolbar
            {
                const string k_Base = k_EntityHierarchyBase + "-toolbar";
                public const string Container = k_Base + "__container";
                public const string LeftSide = k_Base + "__left";
                public const string RightSide = k_Base + "__right";
                public const string SearchField = k_Base + "__search-field";
            }

            public static class Item
            {
                const string k_Base = k_EntityHierarchyBase + "-item";

                public const string SceneNode = k_Base + "__scene-node";

                public const string Icon = k_Base + "__icon";
                public const string IconScene = Icon + "--scene";
                public const string IconEntity = Icon + "--entity";

                public const string NameLabel = k_Base + "__name-label";
                public const string NameScene = NameLabel + "--scene";

                public const string SystemButton = k_Base + "__system-button";
                public const string PingGameObjectButton = k_Base + "__ping-gameobject-button";

                public const string VisibleOnHover = k_Base + "__visible-on-hover";
            }

            public static class SearchEmptyMessage
            {
                const string k_Base = k_EntityHierarchyBase + "-search-empty-message";

                public const string Title = k_Base + "__title";
                public const string Message = k_Base + "__message";
            }
        }
    }
}
