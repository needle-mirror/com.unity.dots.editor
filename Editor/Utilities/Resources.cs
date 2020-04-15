using UnityEditor;

namespace Unity.Entities.Editor
{
    static class Resources
    {
        public const string Uxml = Constants.EditorDefaultResourcesPath + "uxml/";
        public const string Uss = Constants.EditorDefaultResourcesPath + "uss/";
        public const string Icons = Constants.EditorDefaultResourcesPath + "icons/";

        const string k_ProSuffix = "_dark";
        const string k_PersonalSuffix = "_light";

        public static string SkinSuffix => EditorGUIUtility.isProSkin ? k_ProSuffix : k_PersonalSuffix;

        public static string UxmlFromName(string name)
        {
            return Uxml + name + ".uxml";
        }

        public static string UssFromName(string name)
        {
            return Uss + name + ".uss";
        }

        public static class Templates
        {
            public static readonly UITemplate CommonResources = new UITemplate("Common/common-resources");
            public static readonly UITemplate SystemSchedule = new UITemplate("SystemSchedule/system-schedule");
            public static readonly UITemplate SystemScheduleItem = new UITemplate("SystemSchedule/system-schedule-item");
            public static readonly UITemplate SystemScheduleDetailHeader = new UITemplate("SystemSchedule/system-schedule-detail-header");
            public static readonly UITemplate SystemScheduleDetailContent = new UITemplate("SystemSchedule/system-schedule-detail-content");
            public static readonly UITemplate SystemScheduleDetailQuery = new UITemplate("SystemSchedule/system-schedule-detail-query");
            public static readonly UITemplate CustomToolbarToggle = new UITemplate("Common/custom-toolbar-toggle");
            public static readonly UITemplate Settings = new UITemplate("Settings/settings");
        }
    }
}
