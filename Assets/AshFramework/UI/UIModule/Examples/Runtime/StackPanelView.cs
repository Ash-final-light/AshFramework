using TMPro;
using UnityEngine;

namespace AshFramework.UI.UIModule.Examples.StackDemo
{
    public abstract class StackPanelView : UIView
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _message;
        [SerializeField] private UnityEngine.UI.Button _hideButton;
        [SerializeField] private UnityEngine.UI.Button _closeButton;

        public UnityEngine.UI.Button HideButton => _hideButton;
        public UnityEngine.UI.Button CloseButton => _closeButton;

        public void Configure(TMP_Text title, TMP_Text message, UnityEngine.UI.Button hideButton, UnityEngine.UI.Button closeButton)
        {
            _title = title;
            _message = message;
            _hideButton = hideButton;
            _closeButton = closeButton;
        }

        protected void RenderPanel(string title, string message)
        {
            _title.text = title;
            _message.text = message;
        }
    }
}
