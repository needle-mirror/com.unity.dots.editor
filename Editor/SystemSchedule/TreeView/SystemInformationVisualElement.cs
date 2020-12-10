using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class SystemInformationVisualElement : BindableElement, IBinding
    {
        internal static readonly BasicPool<SystemInformationVisualElement> Pool = new BasicPool<SystemInformationVisualElement>(() => new SystemInformationVisualElement());

        public World World;
        SystemTreeViewItem m_Target;
        public SystemTreeView TreeView { get; set; }
        const float k_SystemNameLabelWidth = 100f;
        const float k_SingleIndentWidth = 12f;

        public SystemTreeViewItem Target
        {
            get => m_Target;
            set
            {
                if (m_Target == value)
                    return;
                m_Target = value;
                Update();
            }
        }

        readonly Toggle m_SystemEnableToggle;
        readonly VisualElement m_Icon;
        readonly Label m_SystemNameLabel;
        readonly Label m_EntityMatchLabel;
        readonly Label m_RunningTimeLabel;

        SystemInformationVisualElement()
        {
            Resources.Templates.CommonResources.AddStyles(this);
            Resources.Templates.SystemScheduleItem.Clone(this);
            binding = this;

            AddToClassList(UssClasses.DotsEditorCommon.CommonResources);
            AddToClassList(UssClasses.Resources.SystemSchedule);

            m_SystemEnableToggle = this.Q<Toggle>(className: UssClasses.SystemScheduleWindow.Items.Enabled);
            m_SystemEnableToggle.RegisterCallback<ChangeEvent<bool>, SystemInformationVisualElement>(
                OnSystemTogglePress, this);

            m_Icon = this.Q(className: UssClasses.SystemScheduleWindow.Items.Icon);

            m_SystemNameLabel = this.Q<Label>(className: UssClasses.SystemScheduleWindow.Items.SystemName);
            m_EntityMatchLabel = this.Q<Label>(className: UssClasses.SystemScheduleWindow.Items.Matches);
            m_RunningTimeLabel = this.Q<Label>(className: UssClasses.SystemScheduleWindow.Items.Time);
        }

        public static SystemInformationVisualElement Acquire(SystemTreeView treeView, World world)
        {
            var item = Pool.Acquire();

            item.TreeView = treeView;
            item.World = world;
            return item;
        }

        public void Release()
        {
            World = null;
            Target = null;
            TreeView = null;
            Pool.Release(this);
        }

        static void SetText(Label label, string text)
        {
            if (label.text != text)
                label.text = text;
        }

        public unsafe void Update()
        {
            if (null == Target)
                return;

            if (Target.SystemHandle.Valid && Target.SystemHandle.World == null)
                return;

            var ptr = Target.SystemHandle.StatePointer;
            m_Icon.style.display = string.Empty == GetSystemClass(Target.SystemHandle) ? DisplayStyle.None : DisplayStyle.Flex;

            SetText(m_SystemNameLabel, Target.GetSystemName(World));
            SetSystemNameLabelWidth(m_SystemNameLabel, k_SystemNameLabelWidth);
            SetText(m_EntityMatchLabel, Target.GetEntityMatches());
            SetText(m_RunningTimeLabel, Target.GetRunningTime());
            SetSystemClass(m_Icon, Target.SystemHandle);
            SetGroupNodeLabelBold(m_SystemNameLabel, Target.SystemHandle);

            if (Target.SystemHandle == null) // player loop system without children
            {
                SetEnabled(Target.HasChildren);
                m_SystemEnableToggle.style.display = DisplayStyle.None;
            }
            else
            {
                SetEnabled(true);
                m_SystemEnableToggle.style.display = DisplayStyle.Flex;
                var systemState = ptr == null || ptr->Enabled;

                if (m_SystemEnableToggle.value != systemState)
                    m_SystemEnableToggle.SetValueWithoutNotify(systemState);

                var groupState = systemState && Target.GetParentState();

                m_SystemNameLabel.SetEnabled(groupState);
                m_EntityMatchLabel.SetEnabled(groupState);
                m_RunningTimeLabel.SetEnabled(groupState);
            }
        }

        void SetSystemNameLabelWidth(VisualElement label, float fixedWidth)
        {
            var treeViewItemVisualElement = parent?.parent;
            var itemIndentsContainerName = treeViewItemVisualElement?.Q("unity-tree-view__item-indents");
            label.style.width = itemIndentsContainerName == null ? fixedWidth : fixedWidth - itemIndentsContainerName.childCount * k_SingleIndentWidth;
        }

        static void SetSystemClass(VisualElement element, SystemHandle systemHandle)
        {
            if (systemHandle == null)
            {
                element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.CommandBufferIcon);
                element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemGroupIcon);
                element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemIcon);
                element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.UnmanagedSystemIcon);
                return;
            }

            if (systemHandle.Managed != null)
            {
                switch (systemHandle.Managed)
                {
                    case EntityCommandBufferSystem _:
                        element.AddToClassList(UssClasses.SystemScheduleWindow.Items.CommandBufferIcon);
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemGroupIcon);
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemIcon);
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.UnmanagedSystemIcon);
                        break;
                    case ComponentSystemGroup _:
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.CommandBufferIcon);
                        element.AddToClassList(UssClasses.SystemScheduleWindow.Items.SystemGroupIcon);
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemIcon);
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.UnmanagedSystemIcon);
                        break;
                    case ComponentSystemBase _:
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.CommandBufferIcon);
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemGroupIcon);
                        element.AddToClassList(UssClasses.SystemScheduleWindow.Items.SystemIcon);
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.UnmanagedSystemIcon);
                        break;
                }
            }
            else if (systemHandle.Unmanaged.World != null)
            {
                element.AddToClassList(UssClasses.SystemScheduleWindow.Items.UnmanagedSystemIcon);
                element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.CommandBufferIcon);
                element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemGroupIcon);
                element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemIcon);
            }
        }

        static void SetGroupNodeLabelBold(VisualElement element, SystemHandle systemHandle)
        {
            if (systemHandle == null)
            {
                element.AddToClassList(UssClasses.SystemScheduleWindow.Items.SystemNameBold);
                element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemNameNormal);
                return;
            }

            if (systemHandle.Managed != null)
            {
                switch (systemHandle.Managed)
                {
                    case ComponentSystemGroup _:
                        element.AddToClassList(UssClasses.SystemScheduleWindow.Items.SystemNameBold);
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemNameNormal);
                        break;
                    case EntityCommandBufferSystem _:
                    case ComponentSystemBase _:
                        element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemNameBold);
                        element.AddToClassList(UssClasses.SystemScheduleWindow.Items.SystemNameNormal);
                        break;
                }
            }
            else if (systemHandle.Unmanaged.World != null)
            {
                element.RemoveFromClassList(UssClasses.SystemScheduleWindow.Items.SystemNameBold);
                element.AddToClassList(UssClasses.SystemScheduleWindow.Items.SystemNameNormal);
            }
        }

        static string GetSystemClass(SystemHandle systemHandle)
        {
            if (systemHandle == null)
                return string.Empty;

            if (systemHandle.Managed != null)
            {
                switch (systemHandle.Managed)
                {
                    case EntityCommandBufferSystem _:
                        return UssClasses.SystemScheduleWindow.Items.CommandBufferIcon;
                    case ComponentSystemGroup _:
                        return UssClasses.SystemScheduleWindow.Items.SystemGroupIcon;
                    case ComponentSystemBase _:
                        return UssClasses.SystemScheduleWindow.Items.SystemIcon;
                }
            }
            else if (systemHandle.Unmanaged.World != null)
            {
                return UssClasses.SystemScheduleWindow.Items.UnmanagedSystemIcon;
            }

            return string.Empty;
        }

        static void OnSystemTogglePress(ChangeEvent<bool> evt, SystemInformationVisualElement item)
        {
            if (item.Target.SystemHandle != null)
            {
                item.Target.SetSystemState(evt.newValue);
            }
            else
            {
                item.Target.SetPlayerLoopSystemState(evt.newValue);
            }
        }

        void IBinding.PreUpdate() { }

        void IBinding.Release() { }
    }
}
