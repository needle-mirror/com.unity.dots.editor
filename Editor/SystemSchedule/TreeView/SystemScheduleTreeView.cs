using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using Unity.Editor.Bridge;

namespace Unity.Entities.Editor
{
    class SystemScheduleTreeView : VisualElement
    {
        public readonly TreeView SystemTreeView;
        public readonly VisualElement SystemDetailsContainer = new VisualElement();
        readonly IList<ITreeViewItem> m_TreeRootItems = new List<ITreeViewItem>();
        World m_World;

        public string SearchFilter { get; set; }

        /// <summary>
        /// Constructor of the tree view.
        /// </summary>
        public SystemScheduleTreeView()
        {
            SystemTreeView = new TreeView(m_TreeRootItems, 20, MakeItem, BindItem);
            SystemTreeView.style.flexGrow = 1;
            SystemTreeView.Filter = OnFilter;
            Add(SystemTreeView);

            // System details.
            SystemTreeView.onSelectionChange += (selectedItems) =>
            {
                var item = selectedItems.OfType<SystemTreeViewItem>().FirstOrDefault();
                if (null == item)
                    return;

                SystemDetailsContainer.Clear();
                var bottomSystemDetails = new SystemDetails(item);
                if (bottomSystemDetails.systemDetailContainer != null)
                {
                    SystemDetailsContainer.Add(bottomSystemDetails.systemDetailContainer);
                    Add(SystemDetailsContainer);
                }
            };
        }

        VisualElement MakeItem()
        {
            var systemItem = SystemSchedulePool.GetSystemInformationVisualElement(this);
            systemItem.World = m_World;
            return systemItem;
        }

        public void Refresh(World world, bool showInactiveSystems)
        {
            m_World = world;
            foreach (var root in m_TreeRootItems.OfType<SystemTreeViewItem>())
            {
                root.ReturnToPool();
            }
            m_TreeRootItems.Clear();

            var graph = PlayerLoopSystemGraph.Current;

            foreach (var node in graph.Roots)
            {
                if (!node.ShowForWorld(m_World))
                    continue;

                if (!node.IsRunning && !showInactiveSystems)
                    continue;

                var i = SystemSchedulePool.GetSystemTreeViewItem(graph, node, null, m_World, showInactiveSystems);
                m_TreeRootItems.Add(i);
            }

            Refresh();
        }

        /// <summary>
        /// Refresh tree view to update with latest information.
        /// </summary>
        void Refresh()
        {
            // This is needed because `ListView.Refresh` will re-create all the elements.
            SystemSchedulePool.ReturnAllToPool(this);
            SystemTreeView.Refresh();
        }

        void BindItem(VisualElement element, ITreeViewItem item)
        {
            var target = item as SystemTreeViewItem;
            var systemInformationElement = element as SystemInformationVisualElement;
            if (null == systemInformationElement)
                return;

            systemInformationElement.Target = target;
            systemInformationElement.World = m_World;
            systemInformationElement.Update();
        }

        bool OnFilter(ITreeViewItem item)
        {
            if (string.IsNullOrEmpty(SearchFilter))
                return true;

            var itemAsData = item as SystemTreeViewItem;
            if (itemAsData == null)
                return false;

            if (itemAsData.GetSystemName(m_World).IndexOf(SearchFilter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }
    }
}
