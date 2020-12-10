using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class TabContent : VisualElement
    {
        static readonly string s_UssClassName = "tab-element";

        public string TabName { get; set; }

        public TabContent()
        {
            AddToClassList(s_UssClassName);
        }
    }
}
