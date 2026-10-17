using System;

namespace AshFramework.UI.UIModule.Examples.StackDemo
{
    public sealed class StackFirstWindow : StackFirstWindowBase
    {
        private readonly StackWindowStatistics _statistics;
        private readonly Action _changed;
        private bool _created;

        public StackFirstWindow(StackWindowStatistics statistics, Action changed = null)
        {
            _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            _changed = changed;
        }

        protected override void OnInitialize()
        {
            _statistics.Created();
            _created = true;
            View.HideButton.onClick.AddListener(HideClicked);
            View.CloseButton.onClick.AddListener(CloseClicked);
        }

        protected override void OnBind(StackFirstArgs args)
        {
            _statistics.Bound();
            View.Render(args);
        }

        protected override void OnDispose()
        {
            if (View != null)
            {
                View.HideButton.onClick.RemoveListener(HideClicked);
                View.CloseButton.onClick.RemoveListener(CloseClicked);
            }

            if (_created)
            {
                _statistics.Released();
                _created = false;
            }
        }

        private void HideClicked()
        {
            StackExampleActions.Observe(Context.UI.HideAsync(Key), _changed);
        }

        private void CloseClicked()
        {
            StackExampleActions.Observe(Context.CloseAsync(), _changed);
        }
    }
}
