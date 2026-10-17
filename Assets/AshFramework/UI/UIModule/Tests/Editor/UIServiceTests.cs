using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AshFramework.UI.UIModule.Tests
{
    public sealed class UIServiceTests
    {
        private static readonly UIKey<int> First = new UIKey<int>("First");
        private static readonly UIKey<int> Second = new UIKey<int>("Second");
        private RootUI _root;
        private UIService _ui;
        private ControlledLoader _loader;
        private List<CountingWindow> _windows;
        private Action<CountingWindow> _configureWindow;

        [SetUp]
        public void SetUp()
        {
            var rootObject = new GameObject("UIModuleTestRoot", typeof(RectTransform), typeof(RootUI));
            _root = rootObject.GetComponent<RootUI>();
            Canvas main = CreateCanvas("Main", 0);
            Canvas popup = CreateCanvas("Popup", 100);
            Canvas overlay = CreateCanvas("Overlay", 200);
            var staging = new GameObject("Staging", typeof(RectTransform));
            staging.transform.SetParent(_root.transform, false);
            staging.SetActive(false);
            _root.Configure(main, popup, overlay, (RectTransform)staging.transform);
            _loader = new ControlledLoader();
            _windows = new List<CountingWindow>();
            _configureWindow = null;
            var registry = new UIRegistry();
            registry.Register(First, new UIDefinition("First", UILayer.Popup), CreateWindow);
            registry.Register(Second, new UIDefinition("Second", UILayer.Popup), CreateWindow);
            _ui = _root.CreateService(registry, _loader);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Task shutdown = _ui.ShutdownAsync();
            _loader.CompleteOutstanding();

            yield return Settled(shutdown);

            // Individual failure tests observe cleanup errors; teardown still removes its own objects.
            _ = shutdown.Exception;
            Object.DestroyImmediate(_root.gameObject);
        }

        [UnityTest]
        public IEnumerator LoadHideShowAndCloseHaveDistinctLifetimes()
        {
            Assert.That(_ui.TryGet(First, out _), Is.False);
            Assert.That(_loader.Requests.Count, Is.Zero);
            Task<IUIHandle> load = _ui.LoadAsync(First);
            _loader.Complete(0);
            yield return Await(load);
            IUIHandle handle = load.Result;
            Assert.That(handle.State, Is.EqualTo(UIState.Hidden));
            Assert.That(_ui.GetTop(UILayer.Popup), Is.Null);

            yield return Await(_ui.ShowAsync(First, 2));
            yield return Await(_ui.ShowAsync(First, 3));
            CountingWindow window = _windows[0];
            Assert.That(window.Bindings, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(window.ShowCount, Is.EqualTo(1));
            yield return Await(_ui.HideAsync(First));
            Assert.That(handle.State, Is.EqualTo(UIState.Hidden));
            Assert.That(_ui.GetTop(UILayer.Popup), Is.Null);
            Assert.That(_loader.Leases[0].ReleaseCalls, Is.Zero);

            yield return Await(_ui.ShowAsync(First, 4));
            Assert.That(window.CreateCount, Is.EqualTo(1));
            Assert.That(window.ShowCount, Is.EqualTo(2));
            yield return Await(_ui.CloseAsync(First));
            yield return Await(_ui.CloseAsync(First));
            Assert.That(handle.State, Is.EqualTo(UIState.Closed));
            Assert.That(window.HideCount, Is.EqualTo(2));
            Assert.That(window.ReleaseCount, Is.EqualTo(1));
            Assert.That(_loader.Leases[0].ReleaseCalls, Is.EqualTo(1));
            Assert.That(_ui.TryGet(First, out _), Is.False);
        }

        [UnityTest]
        public IEnumerator ConcurrentShowsShareOneLoadAndBindInRequestOrder()
        {
            Task<IUIHandle> first = _ui.ShowAsync(First, 10);
            Task<IUIHandle> second = _ui.ShowAsync(First, 20);
            Assert.That(_loader.Requests.Count, Is.EqualTo(1));
            _loader.Complete(0);
            yield return Await(Task.WhenAll(first, second));
            Assert.That(first.Result, Is.SameAs(second.Result));
            Assert.That(_windows[0].Bindings, Is.EqualTo(new[] { 10, 20 }));
            Assert.That(_windows[0].ShowCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LayerOrderUsesRequestSequenceInsteadOfLoadCompletion()
        {
            Task<IUIHandle> first = _ui.ShowAsync(First, 1);
            Task<IUIHandle> second = _ui.ShowAsync(Second, 2);
            _loader.Complete(1);
            yield return Await(second);
            _loader.Complete(0);
            yield return Await(first);
            Assert.That(_ui.GetTop(UILayer.Popup), Is.SameAs(second.Result));
            Assert.That(_loader.Leases[1].Instance.transform.GetSiblingIndex(),
                Is.GreaterThan(_loader.Leases[0].Instance.transform.GetSiblingIndex()));
            yield return Await(_ui.ShowAsync(First, 3));
            Assert.That(_ui.GetTop(UILayer.Popup), Is.SameAs(first.Result));
            yield return Await(_ui.HideAsync(First));
            Assert.That(_ui.GetTop(UILayer.Popup), Is.SameAs(second.Result));
        }

        [UnityTest]
        public IEnumerator CancelOneWaiterDoesNotCancelSharedLoad()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                Task<IUIHandle> first = _ui.ShowAsync(First, 1, cancellation.Token);
                Task<IUIHandle> second = _ui.ShowAsync(First, 2);
                cancellation.Cancel();
                yield return Settled(first);
                _loader.Complete(0);
                yield return Await(second);
                Assert.That(first.IsCanceled, Is.True);
                Assert.That(_loader.Requests[0].Cancellation.IsCancellationRequested, Is.False);
                Assert.That(_windows[0].Bindings, Is.EqualTo(new[] { 2 }));
            }
        }

        [UnityTest]
        public IEnumerator CancelAllWaitersReleasesLateInstanceWithoutCreatingWindow()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                Task<IUIHandle> show = _ui.ShowAsync(First, 1, cancellation.Token);
                cancellation.Cancel();
                yield return Settled(show);
                Task close = _ui.CloseAsync(First);
                _loader.Complete(0);
                yield return Await(close);
                Assert.That(show.IsCanceled, Is.True);
                Assert.That(_windows.Count, Is.Zero);
                Assert.That(_loader.Leases[0].ReleaseCalls, Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator HideDuringLoadCancelsShowButPreservesExplicitLoad()
        {
            Task<IUIHandle> load = _ui.LoadAsync(First);
            Task<IUIHandle> show = _ui.ShowAsync(First, 1);
            yield return Await(_ui.HideAsync(First));
            _loader.Complete(0);
            yield return Await(load);
            Assert.That(show.IsCanceled, Is.True);
            Assert.That(load.Result.State, Is.EqualTo(UIState.Hidden));
            Assert.That(_windows[0].Bindings, Is.Empty);
        }

        [UnityTest]
        public IEnumerator CloseDuringLoadWaitsForCleanupAndReopenGetsNewInstance()
        {
            Task<IUIHandle> oldShow = _ui.ShowAsync(First, 1);
            Task close = _ui.CloseAsync(First);
            Task<IUIHandle> reopen = _ui.ShowAsync(First, 2);
            Assert.That(close.IsCompleted, Is.False);
            Assert.That(_loader.Requests.Count, Is.EqualTo(1));
            _loader.Complete(0);
            yield return Await(close);

            float deadline = Time.realtimeSinceStartup + 5f;

            while (_loader.Requests.Count < 2 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(_loader.Requests.Count, Is.EqualTo(2));
            _loader.Complete(1);
            yield return Await(reopen);
            Assert.That(oldShow.IsCanceled, Is.True);
            Assert.That(_loader.Leases[0].ReleaseCalls, Is.EqualTo(1));
            Assert.That(_windows.Count, Is.EqualTo(1));
            Assert.That(_windows[0].Bindings, Is.EqualTo(new[] { 2 }));
        }

        [UnityTest]
        public IEnumerator ReentrantCloseIsQueuedAndDoesNotDeadlockShow()
        {
            Task callbackClose = null;
            _configureWindow = window => window.ShowAction = () => callbackClose = window.SavedContext.CloseAsync();
            Task<IUIHandle> show = _ui.ShowAsync(First, 1);
            _loader.Complete(0);
            yield return Await(show);
            Assert.That(callbackClose, Is.Not.Null);
            yield return Await(callbackClose);
            Assert.That(show.Result.State, Is.EqualTo(UIState.Closed));
            Assert.That(_windows[0].ReleaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OldInstanceContextCannotCloseReopenedWindow()
        {
            Task<IUIHandle> first = _ui.ShowAsync(First, 1);
            _loader.Complete(0);
            yield return Await(first);
            UIWindowContext oldContext = _windows[0].SavedContext;
            yield return Await(_ui.CloseAsync(First));
            Task<IUIHandle> reopen = _ui.ShowAsync(First, 2);
            _loader.Complete(1);
            yield return Await(reopen);
            yield return Await(oldContext.CloseAsync());
            Assert.That(reopen.Result.State, Is.EqualTo(UIState.Visible));
            Assert.That(first.Result, Is.Not.SameAs(reopen.Result));
        }

        [UnityTest]
        public IEnumerator CreateFailureReleasesPartialInitializationAndAllowsRetry()
        {
            _configureWindow = window => window.FailCreate = true;
            Task<IUIHandle> show = _ui.ShowAsync(First, 1);
            _loader.Complete(0);
            yield return Settled(show);
            Assert.That(show.IsFaulted, Is.True);
            _ = show.Exception;
            yield return Await(_ui.CloseAsync(First));
            Assert.That(_windows[0].ReleaseCount, Is.EqualTo(1));
            Assert.That(_loader.Leases[0].ReleaseCalls, Is.EqualTo(1));
            _configureWindow = null;
            Task<IUIHandle> retry = _ui.ShowAsync(First, 2);
            _loader.Complete(1);
            yield return Await(retry);
            Assert.That(retry.Result.State, Is.EqualTo(UIState.Visible));
        }

        [UnityTest]
        public IEnumerator ReleaseFailureKeepsBarrierAndExplicitCloseRetriesOnlyResourceCleanup()
        {
            Task<IUIHandle> show = _ui.ShowAsync(First, 1);
            _loader.Complete(0);
            yield return Await(show);
            _loader.Leases[0].FailReleaseOnce = true;
            Task close = _ui.CloseAsync(First);
            yield return Settled(close);
            Assert.That(close.IsFaulted, Is.True);
            _ = close.Exception;
            Assert.That(show.Result.State, Is.EqualTo(UIState.Closing));
            Task<IUIHandle> blocked = _ui.ShowAsync(First, 2);
            yield return Settled(blocked);
            Assert.That(blocked.IsFaulted, Is.True);
            _ = blocked.Exception;
            yield return Await(_ui.CloseAsync(First));
            Assert.That(show.Result.State, Is.EqualTo(UIState.Closed));
            Assert.That(_windows[0].ReleaseCount, Is.EqualTo(1));
            Assert.That(_loader.Leases[0].ReleaseCalls, Is.EqualTo(2));
        }

        private CountingWindow CreateWindow()
        {
            var window = new CountingWindow();
            _configureWindow?.Invoke(window);
            _windows.Add(window);
            return window;
        }

        private Canvas CreateCanvas(string name, int order)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            gameObject.transform.SetParent(_root.transform, false);
            Canvas canvas = gameObject.GetComponent<Canvas>();
            canvas.sortingOrder = order;
            return canvas;
        }

        private static IEnumerator Await(Task task)
        {
            yield return Settled(task);
            task.GetAwaiter().GetResult();
        }

        private static IEnumerator Settled(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 5f;

            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "UI operation did not reach a terminal state.");
        }

        private sealed class CountingWindow : UIWindow<int>
        {
            public readonly List<int> Bindings = new List<int>();
            public int CreateCount;
            public int ShowCount;
            public int HideCount;
            public int ReleaseCount;
            public bool FailCreate;
            public Action ShowAction;
            public UIWindowContext SavedContext;

            protected override void OnCreate(UIView view)
            {
                CreateCount++;
                SavedContext = Context;

                if (FailCreate)
                {
                    throw new InvalidOperationException("Injected create failure.");
                }
            }

            protected override void OnBind(int args)
            {
                Bindings.Add(args);
            }

            protected override void OnShow()
            {
                ShowCount++;
                ShowAction?.Invoke();
            }

            protected override void OnHide()
            {
                HideCount++;
            }

            protected override void OnRelease()
            {
                ReleaseCount++;
            }
        }

        private sealed class ControlledLoader : IUIViewLoader
        {
            public readonly List<LoadRequest> Requests = new List<LoadRequest>();
            public readonly Dictionary<int, TestLease> Leases = new Dictionary<int, TestLease>();

            public Task<IUIViewLease> InstantiateAsync(string assetKey, Transform inactiveStagingRoot, CancellationToken cancellationToken)
            {
                var request = new LoadRequest(inactiveStagingRoot, cancellationToken);
                Requests.Add(request);
                return request.Completion.Task;
            }

            public void Complete(int index)
            {
                LoadRequest request = Requests[index];

                if (request.Completion.Task.IsCompleted)
                {
                    return;
                }

                var instance = new GameObject("Window" + index, typeof(RectTransform));
                instance.SetActive(false);
                instance.transform.SetParent(request.Parent, false);
                instance.AddComponent<TestUIView>();
                var lease = new TestLease(instance);
                Leases.Add(index, lease);
                request.Completion.SetResult(lease);
            }

            public void CompleteOutstanding()
            {
                for (int index = 0; index < Requests.Count; index++)
                {
                    Complete(index);
                }
            }
        }

        private sealed class LoadRequest
        {
            public readonly Transform Parent;
            public readonly CancellationToken Cancellation;
            public readonly TaskCompletionSource<IUIViewLease> Completion = new TaskCompletionSource<IUIViewLease>();

            public LoadRequest(Transform parent, CancellationToken cancellation)
            {
                Parent = parent;
                Cancellation = cancellation;
            }
        }

        private sealed class TestLease : IUIViewLease
        {
            public GameObject Instance { get; private set; }
            public int ReleaseCalls;
            public bool FailReleaseOnce;

            public TestLease(GameObject instance)
            {
                Instance = instance;
            }

            public Task ReleaseAsync()
            {
                ReleaseCalls++;

                if (FailReleaseOnce)
                {
                    FailReleaseOnce = false;
                    throw new InvalidOperationException("Injected release failure.");
                }

                Object.DestroyImmediate(Instance);
                Instance = null;
                return Task.CompletedTask;
            }
        }
    }
}
