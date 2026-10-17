using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using AshFramework.UI.UIModule.Examples.StackDemo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace AshFramework.UI.UIModule.Examples.Tests
{
    public sealed class StackExamplePlayModeTests
    {
        private RootUI _root;
        private UIService _ui;
        private StackWindowStatistics _first;
        private StackWindowStatistics _second;
        private GameObject _ownedEventSystem;

        [SetUp]
        public void SetUp()
        {
            var rootObject = new GameObject("StackExampleTestRoot", typeof(RectTransform), typeof(RootUI));
            _root = rootObject.GetComponent<RootUI>();
            Canvas main = CreateCanvas("Main", 0);
            Canvas popup = CreateCanvas("Popup", 100);
            Canvas overlay = CreateCanvas("Overlay", 200);
            var staging = new GameObject("Staging", typeof(RectTransform));
            staging.transform.SetParent(_root.transform, false);
            staging.SetActive(false);
            _root.Configure(main, popup, overlay, (RectTransform)staging.transform);
            _first = new StackWindowStatistics();
            _second = new StackWindowStatistics();
            var registry = new UIRegistry();
            StackFirstWindowBase.Register(registry, () => new StackFirstWindow(_first));
            StackSecondWindowBase.Register(registry, () => new StackSecondWindow(_second));
            _ui = _root.CreateService(registry);

            if (EventSystem.current == null)
            {
                _ownedEventSystem = new GameObject("StackExampleTestEventSystem", typeof(EventSystem));
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return Await(_ui.ShutdownAsync());
            Object.Destroy(_root.gameObject);

            if (_ownedEventSystem != null)
            {
                Object.Destroy(_ownedEventSystem);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RealResourcesPreserveOrderReuseHiddenInstanceAndReleaseClosedInstance()
        {
            Task<IUIHandle> first = _ui.ShowAsync(StackFirstWindowBase.Key, new StackFirstArgs("First arguments"));
            yield return Await(first);
            Task<IUIHandle> second = _ui.ShowAsync(StackSecondWindowBase.Key, new StackSecondArgs("Second arguments"));
            yield return Await(second);
            Assert.That(_ui.GetTop(UILayer.Popup), Is.SameAs(second.Result));
            StackFirstView firstView = _root.PopupCanvas.GetComponentInChildren<StackFirstView>(true);
            StackSecondView secondView = _root.PopupCanvas.GetComponentInChildren<StackSecondView>(true);
            Assert.That(firstView, Is.Not.Null);
            Assert.That(secondView, Is.Not.Null);
            Assert.That(secondView.transform.GetSiblingIndex(), Is.GreaterThan(firstView.transform.GetSiblingIndex()));

            yield return Await(_ui.ShowAsync(StackFirstWindowBase.Key, new StackFirstArgs("Updated arguments")));
            Assert.That(_ui.GetTop(UILayer.Popup), Is.SameAs(first.Result));
            Assert.That(_first.Creates, Is.EqualTo(1));
            Assert.That(_first.Binds, Is.EqualTo(2));
            yield return Await(_ui.HideAsync(StackFirstWindowBase.Key));
            Assert.That(_ui.GetTop(UILayer.Popup), Is.SameAs(second.Result));
            Assert.That(first.Result.State, Is.EqualTo(UIState.Hidden));
            Assert.That(firstView.gameObject.activeSelf, Is.False);
            Assert.That(_first.Releases, Is.Zero);
            yield return Await(_ui.ShowAsync(StackFirstWindowBase.Key, new StackFirstArgs("Reuse")));
            Assert.That(_root.PopupCanvas.GetComponentInChildren<StackFirstView>(), Is.SameAs(firstView));

            yield return Await(_ui.CloseAsync(StackFirstWindowBase.Key));
            Assert.That(first.Result.State, Is.EqualTo(UIState.Closed));
            Assert.That(firstView == null, Is.True, "The real Resources lease must destroy its instance.");
            Assert.That(_first.Releases, Is.EqualTo(1));
            Task<IUIHandle> reopened = _ui.ShowAsync(StackFirstWindowBase.Key, new StackFirstArgs("New instance"));
            yield return Await(reopened);
            Assert.That(reopened.Result, Is.Not.SameAs(first.Result));
            Assert.That(_first.Creates, Is.EqualTo(2));
            Assert.That(_root.PopupCanvas.GetComponentsInChildren<StackFirstView>(true).Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TopCardReceivesRaycastAndItsButtonsHideAndCloseThroughWindowEvents()
        {
            Task<IUIHandle> first = _ui.ShowAsync(StackFirstWindowBase.Key, new StackFirstArgs("A"));
            yield return Await(first);
            Task<IUIHandle> second = _ui.ShowAsync(StackSecondWindowBase.Key, new StackSecondArgs("B"));
            yield return Await(second);
            yield return null;
            Canvas.ForceUpdateCanvases();
            StackFirstView firstView = _root.PopupCanvas.GetComponentInChildren<StackFirstView>();
            StackSecondView secondView = _root.PopupCanvas.GetComponentInChildren<StackSecondView>();
            Vector2 point = (RectTransformUtility.WorldToScreenPoint(null, firstView.transform.position) +
                RectTransformUtility.WorldToScreenPoint(null, secondView.transform.position)) * 0.5f;
            Assert.That(RaycastPanel(point), Is.SameAs(secondView));

            secondView.HideButton.onClick.Invoke();
            yield return null;
            Assert.That(second.Result.State, Is.EqualTo(UIState.Hidden));
            Assert.That(RaycastPanel(point), Is.SameAs(firstView));
            Assert.That(_second.Releases, Is.Zero);
            yield return Await(_ui.ShowAsync(StackSecondWindowBase.Key, new StackSecondArgs("B again")));
            secondView.CloseButton.onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 10f;

            while (second.Result.State != UIState.Closed && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(second.Result.State, Is.EqualTo(UIState.Closed));
            Assert.That(_second.Releases, Is.EqualTo(1));
            Assert.That(RaycastPanel(point), Is.SameAs(firstView));
        }

        private StackPanelView RaycastPanel(Vector2 position)
        {
            var pointer = new PointerEventData(EventSystem.current) { position = position };
            var hits = new List<RaycastResult>();
            _root.PopupCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>().Raycast(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            return hits[0].gameObject.GetComponentInParent<StackPanelView>();
        }

        private Canvas CreateCanvas(string name, int order)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            gameObject.transform.SetParent(_root.transform, false);
            Canvas canvas = gameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static IEnumerator Await(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 10f;

            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "A real Resources UI operation timed out.");
            task.GetAwaiter().GetResult();
        }
    }
}
