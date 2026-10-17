using System;
using System.Threading;
using System.Threading.Tasks;

namespace AshFramework.UI.UIModule
{
    public sealed class UIWindowContext
    {
        private readonly Func<CancellationToken> _visibleToken;
        private readonly Func<Task> _close;

        public IUIService UI { get; }
        public CancellationToken LifetimeToken { get; }
        public CancellationToken VisibleToken => _visibleToken();

        internal UIWindowContext(
            IUIService ui,
            CancellationToken lifetimeToken,
            Func<CancellationToken> visibleToken,
            Func<Task> close)
        {
            UI = ui;
            LifetimeToken = lifetimeToken;
            _visibleToken = visibleToken;
            _close = close;
        }

        public Task CloseAsync()
        {
            return _close();
        }
    }

    public abstract class UIWindow<TArgs>
    {
        protected UIWindowContext Context { get; private set; }

        protected TView RequireView<TView>(UIView view) where TView : UIView
        {
            if (view is TView typedView)
            {
                return typedView;
            }

            throw new InvalidOperationException($"{GetType().Name} requires a {typeof(TView).Name} on the prefab root.");
        }

        protected virtual void OnCreate(UIView view)
        {
        }

        protected virtual void OnBind(TArgs args)
        {
        }

        protected virtual void OnShow()
        {
        }

        protected virtual void OnHide()
        {
        }

        protected virtual void OnRelease()
        {
        }

        internal void Create(UIWindowContext context, UIView view)
        {
            Context = context;
            OnCreate(view);
        }

        internal void Bind(TArgs args)
        {
            OnBind(args);
        }

        internal void Show()
        {
            OnShow();
        }

        internal void Hide()
        {
            OnHide();
        }

        internal void Release()
        {
            try
            {
                OnRelease();
            }
            finally
            {
                Context = null;
            }
        }
    }
}
