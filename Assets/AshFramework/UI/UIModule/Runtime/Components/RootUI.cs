using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AshFramework.UI.UIModule
{
    [DisallowMultipleComponent]
    [AddComponentMenu("AshFramework/UIModule/RootUI")]
    public sealed class RootUI : MonoBehaviour
    {
        [SerializeField] private Canvas _mainCanvas;
        [SerializeField] private Canvas _popupCanvas;
        [SerializeField] private Canvas _overlayCanvas;
        [SerializeField] private RectTransform _stagingRoot;

        public Canvas MainCanvas => _mainCanvas;
        public Canvas PopupCanvas => _popupCanvas;
        public Canvas OverlayCanvas => _overlayCanvas;
        public Transform StagingRoot => _stagingRoot;
        public UIService Service { get; private set; }

        public void Configure(Canvas main, Canvas popup, Canvas overlay, RectTransform staging)
        {
            if (Service != null)
            {
                throw new InvalidOperationException("Cannot reconfigure a running UI service.");
            }

            _mainCanvas = main;
            _popupCanvas = popup;
            _overlayCanvas = overlay;
            _stagingRoot = staging;
            ValidateConfiguration();
        }

        public UIService CreateService(UIRegistry registry, IUIViewLoader loader = null)
        {
            if (Service != null)
            {
                throw new InvalidOperationException("This RootUI already owns a UI service.");
            }

            ValidateConfiguration();
            Service = new UIService(registry, loader ?? new ResourcesUIViewLoader(), this);
            return Service;
        }

        public void ValidateConfiguration()
        {
            if (_mainCanvas == null || _popupCanvas == null || _overlayCanvas == null || _stagingRoot == null)
            {
                throw new InvalidOperationException("RootUI requires three canvases and an inactive staging root.");
            }

            if (_mainCanvas == _popupCanvas || _mainCanvas == _overlayCanvas || _popupCanvas == _overlayCanvas)
            {
                throw new InvalidOperationException("UI layer canvases must be distinct.");
            }

            if (!(_mainCanvas.sortingOrder < _popupCanvas.sortingOrder &&
                _popupCanvas.sortingOrder < _overlayCanvas.sortingOrder))
            {
                throw new InvalidOperationException("Canvas order must be Main < Popup < Overlay.");
            }

            if (_mainCanvas.sortingLayerID != _popupCanvas.sortingLayerID ||
                _mainCanvas.sortingLayerID != _overlayCanvas.sortingLayerID)
            {
                throw new InvalidOperationException("UI layer canvases must use the same sorting layer.");
            }

            if (_stagingRoot.gameObject.activeSelf)
            {
                throw new InvalidOperationException("StagingRoot must be inactive.");
            }

            foreach (Canvas canvas in new[] { _mainCanvas, _popupCanvas, _overlayCanvas })
            {
                if (canvas.transform.parent != transform || !canvas.gameObject.activeSelf ||
                    canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                {
                    throw new InvalidOperationException("Layer canvases must be active direct children with GraphicRaycaster.");
                }
            }

            if (_stagingRoot.parent != transform)
            {
                throw new InvalidOperationException("StagingRoot must belong to this RootUI.");
            }
        }

        internal Transform GetLayerRoot(UILayer layer)
        {
            switch (layer)
            {
                case UILayer.Main:
                    return _mainCanvas.transform;
                case UILayer.Popup:
                    return _popupCanvas.transform;
                case UILayer.Overlay:
                    return _overlayCanvas.transform;
                default:
                    throw new ArgumentOutOfRangeException(nameof(layer));
            }
        }

        internal static void ClearSelection(GameObject instance)
        {
            EventSystem system = EventSystem.current;

            if (system != null && system.currentSelectedGameObject != null && instance != null &&
                system.currentSelectedGameObject.transform.IsChildOf(instance.transform))
            {
                system.SetSelectedGameObject(null);
            }
        }

        private void OnDestroy()
        {
            if (Service != null)
            {
                ObserveShutdownAsync(Service.ShutdownAsync());
            }
        }

        private static async void ObserveShutdownAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
