using System.Collections.Generic;
using Unity.Entities.Editor.Inspectors;
using Unity.Properties;
using Unity.Properties.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    abstract class ComponentElementBase : BindableElement
    {
        static readonly Dictionary<string, string> s_DisplayNames = new Dictionary<string, string>();

        public ComponentPropertyType Type { get; }
        public string Path { get; }
        protected string DisplayName { get; }
        protected EntityInspectorContext Context { get; }
        protected EntityContainer Container { get; }

        protected ComponentElementBase(IComponentProperty property, EntityInspectorContext context)
        {
            Type = property.Type;
            Path = property.Name;
            DisplayName = GetDisplayName(property.Name);
            Context = context;
            Container = Context.EntityContainer;
        }

        protected PropertyElement CreateContent<TValue>(IComponentProperty property, ref TValue value)
        {
            Resources.Templates.Inspector.InspectorStyle.AddStyles(this);
            AddToClassList(UssClasses.Resources.Inspector);
            AddToClassList(UssClasses.Resources.ComponentIcons);

            InspectorUtility.CreateComponentHeader(this, property.Type, DisplayName);
            var foldout = this.Q<Foldout>(className: UssClasses.Inspector.Component.Header);
            var toggle = foldout.Q<Toggle>();
            var container = Container;

            toggle.AddManipulator(new ContextualMenuManipulator(evt => { OnPopulateMenu(evt.menu); }));

            var componentMenu = this.Q<VisualElement>(className: UssClasses.Inspector.Component.Menu);

            var content = new PropertyElement();
            foldout.contentContainer.Add(content);
            content.AddContext(Context);
            content.SetTarget(value);

            content.OnChanged += OnComponentChanged;

            foldout.contentContainer.AddToClassList(UssClasses.Inspector.Component.Container);

            if (container.IsReadOnly)
            {
                SetReadonly(foldout);
                foldout.RegisterCallback<ClickEvent, EntityInspectorContext>(OnClicked, Context, TrickleDown.TrickleDown);
            }

            return content;
        }

        protected virtual void SetReadonly(VisualElement root)
        {
            root.contentContainer.SetEnabled(false);
        }

        protected abstract void OnComponentChanged(PropertyElement element, PropertyPath path);

        protected abstract void OnPopulateMenu(DropdownMenu menu);

        static void OnClicked(ClickEvent evt, EntityInspectorContext context)
        {
            var element = (VisualElement)evt.target;
            OnClicked(evt, context, element);
        }

        static void OnClicked(ClickEvent evt, EntityInspectorContext context, VisualElement current)
        {
            switch (current)
            {
                case Foldout foldout:
                    if (!foldout.Q<Toggle>().worldBound.Contains(evt.position))
                        break;
                    foldout.value = !foldout.value;
                    break;
                case ObjectField objectField:
                    var display = objectField.Q(className: UssClasses.UIToolkit.ObjectField.Display);
                    if (null == display)
                        break;
                    if (!display.worldBound.Contains(evt.position))
                        break;

                    if (evt.clickCount == 1)
                        EditorGUIUtility.PingObject(objectField.value);
                    else
                    {
                        var value = objectField.value;
                        if (null != value && value)
                            Selection.activeObject = value;
                    }
                    break;
                case EntityField entityField:
                    var input = entityField.Q(className: "unity-entity-field__input");
                    if (null == input)
                        break;
                    if (!input.worldBound.Contains(evt.position))
                        break;

                    if (evt.clickCount > 1)
                    {
                        var world = context.World;
                        if (null == world || !world.IsCreated)
                            break;
                        if (!context.EntityManager.Exists(entityField.value))
                            break;

                        EntitySelectionProxy.SelectEntity(context.World, entityField.value);
                    }
                    break;
            }

            for (var i = 0; i < current.childCount; ++i)
            {
                OnClicked(evt, context, current[i]);
            }
        }

        static string GetDisplayName(string propertyName)
        {
            if (!s_DisplayNames.TryGetValue(propertyName, out var displayName))
            {
                s_DisplayNames[propertyName] = displayName = ObjectNames.NicifyVariableName(propertyName.Replace("<", "[").Replace(">", "]"))
                    .Replace("_", " | ")
                    .Replace("[", "<")
                    .Replace("]", ">");
            }

            return displayName;
        }
    }
}
