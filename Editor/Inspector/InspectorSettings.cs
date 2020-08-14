using JetBrains.Annotations;
using Unity.Editor.Bridge;
using Unity.Properties;

namespace Unity.Entities.Editor
{
    [DOTSEditorPreferencesSetting(Constants.Settings.Inspector), UsedImplicitly]
    class InspectorSettings : ISetting
    {
        public enum InspectorBackend
        {
            [UsedImplicitly] Debug = 0,
            [UsedImplicitly] Normal = 1
        }

        // TODO: Rename this field to Backend when removing the `InternalSetting` and flip the default value to
        // InspectorBackend.Normal. This will force a re-serialization to make it default for users.
        [InternalSetting]
        public InspectorBackend InternalBackend = InspectorBackend.Debug;

        [InternalSetting]
        public bool DisplayComponentType = false;

        void ISetting.OnSettingChanged(PropertyPath path)
        {
            var p = path.ToString();
            if (p == nameof(InternalBackend))
                InspectorWindowBridge.ReloadAllInspectors();
        }
    }
}
