using System;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    interface ITelemetry
    {
        string Name { get; }
    }

    class TelemetryWindow : EditorWindow
    {
        PropertyElement m_PropertyElement;

        public static void AddCustomMenu(ITelemetry instance, GenericMenu menu)
        {
            if (!Unsupported.IsDeveloperMode())
                return;

            menu.AddItem(new GUIContent($"Open {GetName(instance)}"), false, () =>
            {
                var wnd = CreateWindow<TelemetryWindow>();

                wnd.Initialize(instance);
                wnd.Show();
            });
        }

        void Initialize(ITelemetry instance)
        {
            titleContent = new GUIContent(GetName(instance));
            m_PropertyElement.SetTarget(instance);
        }

        static string GetName(ITelemetry instance) => instance.Name ?? instance.GetType().Name;

        void OnEnable()
        {
            var root = Resources.Templates.TelemetryWindow.Clone(rootVisualElement);
            m_PropertyElement = root.Q<PropertyElement>();
        }
    }
}
