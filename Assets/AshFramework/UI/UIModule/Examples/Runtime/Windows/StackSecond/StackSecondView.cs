using UnityEngine;

namespace AshFramework.UI.UIModule.Examples.StackDemo
{
    [DisallowMultipleComponent]
    public sealed class StackSecondView : StackPanelView
    {
        public void Render(StackSecondArgs args)
        {
            RenderPanel("WINDOW B", args.Message);
        }
    }
}
