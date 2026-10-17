using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace AshFramework.UI.UIModule.Examples.StackDemo
{
    public sealed class StackExampleController : MonoBehaviour
    {
        [SerializeField] private RootUI _root;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private UnityEngine.UI.Button _showFirst;
        [SerializeField] private UnityEngine.UI.Button _showSecond;
        [SerializeField] private UnityEngine.UI.Button _hideTop;
        [SerializeField] private UnityEngine.UI.Button _closeTop;
        [SerializeField] private UnityEngine.UI.Button _reset;
        private readonly StackWindowStatistics _first = new StackWindowStatistics();
        private readonly StackWindowStatistics _second = new StackWindowStatistics();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private UIService _ui;
        private bool _busy;

        public void Configure(RootUI root, TMP_Text status, UnityEngine.UI.Button showFirst,
            UnityEngine.UI.Button showSecond, UnityEngine.UI.Button hideTop,
            UnityEngine.UI.Button closeTop, UnityEngine.UI.Button reset)
        {
            _root = root;
            _status = status;
            _showFirst = showFirst;
            _showSecond = showSecond;
            _hideTop = hideTop;
            _closeTop = closeTop;
            _reset = reset;
        }

        private void Start()
        {
            var registry = new UIRegistry();
            StackFirstWindowBase.Register(registry, () => new StackFirstWindow(_first, RefreshStatus));
            StackSecondWindowBase.Register(registry, () => new StackSecondWindow(_second, RefreshStatus));
            _ui = _root.CreateService(registry);
            _showFirst.onClick.AddListener(ShowFirstClicked);
            _showSecond.onClick.AddListener(ShowSecondClicked);
            _hideTop.onClick.AddListener(HideTopClicked);
            _closeTop.onClick.AddListener(CloseTopClicked);
            _reset.onClick.AddListener(ResetClicked);
            ResetClicked();
        }

        public Task ShowFirstAsync()
        {
            return ExecuteAsync(() => _ui.ShowAsync(StackFirstWindowBase.Key, FirstArgs(), _lifetime.Token));
        }

        public Task ShowSecondAsync()
        {
            return ExecuteAsync(() => _ui.ShowAsync(StackSecondWindowBase.Key, SecondArgs(), _lifetime.Token));
        }

        public Task HideTopAsync()
        {
            return ExecuteAsync(() => ChangeTopAsync(false));
        }

        public Task CloseTopAsync()
        {
            return ExecuteAsync(() => ChangeTopAsync(true));
        }

        public Task ResetAsync()
        {
            return ExecuteAsync(async () =>
            {
                await Task.WhenAll(_ui.CloseAsync(StackFirstWindowBase.Key), _ui.CloseAsync(StackSecondWindowBase.Key));
                await _ui.ShowAsync(StackFirstWindowBase.Key, FirstArgs(), _lifetime.Token);
                await _ui.ShowAsync(StackSecondWindowBase.Key, SecondArgs(), _lifetime.Token);
            });
        }

        private async Task ExecuteAsync(Func<Task> action)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            SetControls(false);

            try
            {
                await action();
            }
            finally
            {
                _busy = false;

                if (this != null)
                {
                    SetControls(true);
                    RefreshStatus();
                }
            }
        }

        private Task ChangeTopAsync(bool close)
        {
            IUIHandle top = _ui.GetTop(UILayer.Popup);

            if (top == null)
            {
                return Task.CompletedTask;
            }

            if (top.Id == StackFirstWindowBase.Key.Id)
            {
                return close ? _ui.CloseAsync(StackFirstWindowBase.Key) : _ui.HideAsync(StackFirstWindowBase.Key);
            }

            return close ? _ui.CloseAsync(StackSecondWindowBase.Key) : _ui.HideAsync(StackSecondWindowBase.Key);
        }

        private StackFirstArgs FirstArgs()
        {
            return new StackFirstArgs("Blue / Popup layer\n\nShow A brings this card to the top.\nHide keeps its instance.\nClose releases its instance.");
        }

        private StackSecondArgs SecondArgs()
        {
            return new StackSecondArgs("Coral / Popup layer\n\nBoth windows use the same layer.\nLater Show requests appear above.\nThe lower window stays visible.");
        }

        private void RefreshStatus()
        {
            if (this == null || _status == null || _ui == null)
            {
                return;
            }

            IUIHandle top = _ui.GetTop(UILayer.Popup);
            string topName = top == null ? "NONE" : top.Id == StackFirstWindowBase.Key.Id ? "WINDOW A" : "WINDOW B";
            string firstState = _ui.TryGet(StackFirstWindowBase.Key, out IUIHandle first) ? first.State.ToString() : "Closed";
            string secondState = _ui.TryGet(StackSecondWindowBase.Key, out IUIHandle second) ? second.State.ToString() : "Closed";
            _status.text = $"POPUP TOP: {topName}\n\n" +
                $"A  {firstState}\nCreate {_first.Creates}  Bind {_first.Binds}  Release {_first.Releases}\n\n" +
                $"B  {secondState}\nCreate {_second.Creates}  Bind {_second.Binds}  Release {_second.Releases}";
        }

        private void SetControls(bool interactable)
        {
            foreach (UnityEngine.UI.Button button in new[] { _showFirst, _showSecond, _hideTop, _closeTop, _reset })
            {
                button.interactable = interactable;
            }
        }

        private void ShowFirstClicked()
        {
            StackExampleActions.Observe(ShowFirstAsync());
        }

        private void ShowSecondClicked()
        {
            StackExampleActions.Observe(ShowSecondAsync());
        }

        private void HideTopClicked()
        {
            StackExampleActions.Observe(HideTopAsync());
        }

        private void CloseTopClicked()
        {
            StackExampleActions.Observe(CloseTopAsync());
        }

        private void ResetClicked()
        {
            StackExampleActions.Observe(ResetAsync());
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();

            if (_showFirst != null)
            {
                _showFirst.onClick.RemoveListener(ShowFirstClicked);
                _showSecond.onClick.RemoveListener(ShowSecondClicked);
                _hideTop.onClick.RemoveListener(HideTopClicked);
                _closeTop.onClick.RemoveListener(CloseTopClicked);
                _reset.onClick.RemoveListener(ResetClicked);
            }
        }
    }
}
