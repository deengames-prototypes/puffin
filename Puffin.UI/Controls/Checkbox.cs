using Puffin.Core.Ecs;
using Puffin.Core.Ecs.Components;
using System;
using System.IO;

namespace Puffin.UI.Controls
{
    /// <summary>
    /// Creates a clickable checkbox with specified checked/unchecked image.
    /// </summary>
    public class Checkbox : Entity
    {
        public bool IsChecked
        {
            get { return this.isChecked; }
            set
            {
                this.isChecked = value;
                this.Get<SpriteComponent>().FileName = this.isChecked ? this.checkedImage : this.uncheckedImage;
                this.onToggle?.Invoke();
            }
        }

        private readonly string uncheckedImage;
        private readonly string checkedImage;
        private readonly Action onToggle;
        private bool isChecked = false;

        /// <summary>
        /// Creates a new checkbox.
        /// </summary>
        public Checkbox(bool isUiElement, string uncheckedImage, string checkedImage, int spriteWidth, int spriteHeight, string text, Action onToggle = null)
        : base(isUiElement)
        {
            this.uncheckedImage = uncheckedImage;
            this.checkedImage = checkedImage;
            
            // Get rid of sprite width/height once this.Sprite(...) is enough to get the sprite width/height
            this.Sprite(checkedImage);
            this.Mouse(spriteWidth, spriteHeight, (x, y) => {
                this.IsChecked = !this.IsChecked;
                return false;
            });
            this.Label(text, spriteWidth + 8);
            this.IsChecked = true;

            // Don't fire when we set IsChecked just above, could result in null pointer exception in user code
            this.onToggle = onToggle;
        }
    }
}