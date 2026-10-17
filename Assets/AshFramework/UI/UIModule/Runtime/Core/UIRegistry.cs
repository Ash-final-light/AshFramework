using System;
using System.Collections.Generic;

namespace AshFramework.UI.UIModule
{
    public sealed class UIRegistry
    {
        private readonly Dictionary<string, Registration> _definitions =
            new Dictionary<string, Registration>(StringComparer.Ordinal);

        private bool _frozen;

        public void Register<TArgs>(
            UIKey<TArgs> key,
            UIDefinition definition,
            Func<UIWindow<TArgs>> factory)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("UI registry is frozen after service construction.");
            }

            if (string.IsNullOrWhiteSpace(key.Id))
            {
                throw new ArgumentException("UI key cannot be default.", nameof(key));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            _definitions.Add(key.Id, new Registration(
                definition,
                typeof(TArgs),
                () => new WindowDriver<TArgs>(factory())));
        }

        internal void Freeze()
        {
            _frozen = true;
        }

        internal Registration Resolve<TArgs>(UIKey<TArgs> key)
        {
            if (string.IsNullOrWhiteSpace(key.Id) || !_definitions.TryGetValue(key.Id, out Registration registration))
            {
                throw new KeyNotFoundException($"UI key is not registered: {key.Id}");
            }

            if (registration.ArgsType != typeof(TArgs))
            {
                throw new ArgumentException($"UI argument type does not match registration: {key.Id}");
            }

            return registration;
        }

        internal sealed class Registration
        {
            public readonly UIDefinition Definition;
            public readonly Type ArgsType;
            public readonly Func<IWindowDriver> Factory;

            public Registration(UIDefinition definition, Type argsType, Func<IWindowDriver> factory)
            {
                Definition = definition;
                ArgsType = argsType;
                Factory = factory;
            }
        }
    }

    internal interface IWindowDriver
    {
        void Create(UIWindowContext context, UIView view);
        void Bind(object args);
        void Show();
        void Hide();
        void Release();
    }

    internal sealed class WindowDriver<TArgs> : IWindowDriver
    {
        private readonly UIWindow<TArgs> _window;

        public WindowDriver(UIWindow<TArgs> window)
        {
            _window = window ?? throw new InvalidOperationException("UI window factory returned null.");
        }

        public void Create(UIWindowContext context, UIView view)
        {
            _window.Create(context, view);
        }

        public void Bind(object args)
        {
            _window.Bind((TArgs)args);
        }

        public void Show()
        {
            _window.Show();
        }

        public void Hide()
        {
            _window.Hide();
        }

        public void Release()
        {
            _window.Release();
        }
    }
}
