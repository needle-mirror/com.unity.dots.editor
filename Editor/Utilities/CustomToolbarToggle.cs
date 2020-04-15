using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine;
using Image = UnityEngine.UIElements.Image;

namespace Unity.Entities.Editor
{
    public class CustomToolbarToggle : ToolbarToggle
    {
        /// <summary>
        ///   <para>Constructor.</para>
        /// </summary>
        /// <param name="labelText"> The text for the label. </param>
        /// <param name="image"> The image at the front of the toggle.</param>
        public CustomToolbarToggle(string labelText, Image image = null)
        {
            Resources.Templates.CustomToolbarToggle.AddStyles(this);

            this.text = labelText;
            var label = this.Q<Label>();

            if (image != null)
            {
                this.Insert(0, image);
                image.AddToClassList("custom-toolbar-toggle__icon ");
                label.AddToClassList("custom-toolbar-toggle__label");
            }
            else
            {
                label.AddToClassList("custom-toolbar-toggle__only-label");
            }

            this.AddToClassList("custom-toolbar-toggle");
            label.parent.AddToClassList("custom-toolbar-toggle__label-parent");
        }
    }
}
