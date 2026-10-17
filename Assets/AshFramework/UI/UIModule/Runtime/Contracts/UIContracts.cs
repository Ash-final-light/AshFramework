using System;
using System.Threading;
using System.Threading.Tasks;

namespace AshFramework.UI.UIModule
{
    public readonly struct UIKey<TArgs>
    {
        public string Id { get; }

        public UIKey(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("UI key cannot be empty.", nameof(id));
            }

            Id = id;
        }
    }

    public readonly struct NoArgs
    {
        public static NoArgs Value => default;
    }

    public enum UILayer
    {
        Main,
        Popup,
        Overlay
    }

    public enum UIState
    {
        Loading,
        Hidden,
        Showing,
        Visible,
        Hiding,
        Closing,
        Closed
    }

    public sealed class UIDefinition
    {
        public string AssetKey { get; }
        public UILayer Layer { get; }

        public UIDefinition(string assetKey, UILayer layer)
        {
            if (string.IsNullOrWhiteSpace(assetKey))
            {
                throw new ArgumentException("UI asset key cannot be empty.", nameof(assetKey));
            }

            if (!Enum.IsDefined(typeof(UILayer), layer))
            {
                throw new ArgumentOutOfRangeException(nameof(layer));
            }

            AssetKey = assetKey;
            Layer = layer;
        }
    }

    public interface IUIHandle
    {
        string Id { get; }
        UIState State { get; }
    }

    public interface IUIService
    {
        Task<IUIHandle> LoadAsync<TArgs>(UIKey<TArgs> key, CancellationToken cancellationToken = default);
        Task<IUIHandle> ShowAsync<TArgs>(UIKey<TArgs> key, TArgs args, CancellationToken cancellationToken = default);
        Task HideAsync<TArgs>(UIKey<TArgs> key);
        Task CloseAsync<TArgs>(UIKey<TArgs> key);
        bool TryGet<TArgs>(UIKey<TArgs> key, out IUIHandle handle);
    }
}
