using UnityEngine;

namespace AshFramework.UI.UIModule.Examples.StackDemo
{
    [DisallowMultipleComponent]
    public sealed class StackFirstView : StackPanelView
    {
        public void Render(StackFirstArgs args)
        {
            RenderPanel("WINDOW A", args.Message);
        }
    }
}
