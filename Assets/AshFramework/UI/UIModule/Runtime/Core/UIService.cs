using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AshFramework.UI.UIModule
{
    /// <summary>Main-thread UI coordinator. Resource waits never hold the command queue.</summary>
    public sealed class UIService : IUIService
    {
        private readonly UIRegistry _registry;
        private readonly IUIViewLoader _loader;
        private readonly RootUI _root;
        private readonly UILayerManager _layers = new UILayerManager();
        private readonly Dictionary<string, WindowEntry> _entries = new Dictionary<string, WindowEntry>(StringComparer.Ordinal);
        private readonly Queue<Action> _commands = new Queue<Action>();
        private readonly SynchronizationContext _context;
        private readonly int _threadId;
        private long _generation;
        private long _sequence;
        private bool _draining;
        private bool _shutdown;

        public UIService(UIRegistry registry, IUIViewLoader loader, RootUI root)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _root = root != null ? root : throw new ArgumentNullException(nameof(root));
            _context = SynchronizationContext.Current ??
                throw new InvalidOperationException("Create UIService on the Unity main thread with a synchronization context.");
            _threadId = Thread.CurrentThread.ManagedThreadId;
            _root.ValidateConfiguration();
            _registry.Freeze();
        }

        public Task<IUIHandle> LoadAsync<TArgs>(UIKey<TArgs> key, CancellationToken cancellationToken = default)
        {
            return Submit(key, default(TArgs), false, cancellationToken);
        }

        public Task<IUIHandle> ShowAsync<TArgs>(UIKey<TArgs> key, TArgs args, CancellationToken cancellationToken = default)
        {
            return Submit(key, args, true, cancellationToken);
        }

        public bool TryGet<TArgs>(UIKey<TArgs> key, out IUIHandle handle)
        {
            RequireThread();
            _registry.Resolve(key);
            handle = null;

            if (_entries.TryGetValue(key.Id, out WindowEntry entry) && entry.Prepared && !entry.Retired)
            {
                handle = entry;
                return true;
            }

            return false;
        }

        public IUIHandle GetTop(UILayer layer)
        {
            RequireThread();
            return _layers.GetTop(layer);
        }

        public Task HideAsync<TArgs>(UIKey<TArgs> key)
        {
            RequireThread();
            _registry.Resolve(key);
            var completion = WindowEntry.NewCompletion<bool>();
            Enqueue(() => Hide(key.Id, completion));
            return completion.Task;
        }

        public Task CloseAsync<TArgs>(UIKey<TArgs> key)
        {
            RequireThread();
            _registry.Resolve(key);
            return CloseCurrentAsync(key.Id, null);
        }

        public Task ShutdownAsync()
        {
            RequireThread();
            _shutdown = true;
            var completion = WindowEntry.NewCompletion<bool>();
            Enqueue(() =>
            {
                var tasks = new List<Task>();

                foreach (WindowEntry entry in new List<WindowEntry>(_entries.Values))
                {
                    FailRequests(entry.ReopenRequests, new ObjectDisposedException(nameof(UIService)));
                    tasks.Add(BeginClose(entry));
                }

                ForwardAsync(Task.WhenAll(tasks), completion);
            });
            return completion.Task;
        }

        private Task<IUIHandle> Submit<TArgs>(UIKey<TArgs> key, TArgs args, bool show, CancellationToken token)
        {
            RequireThread();

            if (_shutdown)
            {
                throw new ObjectDisposedException(nameof(UIService));
            }

            UIRegistry.Registration registration = _registry.Resolve(key);
            var request = new UIRequest(key.Id, registration, show, args, show ? ++_sequence : 0, token);

            // Post cancellation even when invoked on the main thread, avoiding inline reentrancy.
            request.CancellationRegistration = token.Register(() =>
            {
                _context.Post(_ => Enqueue(() => CancelRequest(request)), null);
            });

            Enqueue(() => Accept(request));
            return request.Completion.Task;
        }

        private void Accept(UIRequest request)
        {
            if (request.Completion.Task.IsCompleted)
            {
                return;
            }

            if (_shutdown)
            {
                request.Finish(null, new ObjectDisposedException(nameof(UIService)));
                return;
            }

            if (request.Cancellation.IsCancellationRequested)
            {
                request.Finish(null, cancelled: true);
                return;
            }

            if (_entries.TryGetValue(request.Id, out WindowEntry existing))
            {
                request.Entry = existing;

                if (existing.Retired)
                {
                    if (existing.CloseCompletion.Task.IsFaulted)
                    {
                        request.Finish(null, existing.CloseCompletion.Task.Exception);
                    }
                    else
                    {
                        existing.ReopenRequests.Add(request);
                    }

                    return;
                }

                existing.Requests.Add(request);

                if (existing.Prepared)
                {
                    Enqueue(() => ProcessNext(existing));
                }

                return;
            }

            var entry = new WindowEntry(request.Id, ++_generation, request.Registration);
            _entries.Add(entry.Id, entry);
            request.Entry = entry;
            entry.Requests.Add(request);
            LoadEntryAsync(entry);
        }

        private async void LoadEntryAsync(WindowEntry entry)
        {
            try
            {
                IUIViewLease lease = await _loader.InstantiateAsync(
                    entry.Registration.Definition.AssetKey,
                    _root.StagingRoot,
                    entry.LoadCancellation.Token);

                Enqueue(() => FinishLoad(entry, lease, null));
            }
            catch (Exception exception)
            {
                Enqueue(() => FinishLoad(entry, null, exception));
            }
        }

        private void FinishLoad(WindowEntry entry, IUIViewLease lease, Exception loadError)
        {
            entry.Lease = lease;
            entry.LoadFinished.TrySetResult(true);

            if (!IsCurrent(entry))
            {
                // The closing operation owns this late lease and awaits LoadFinished.
                return;
            }

            try
            {
                if (loadError != null)
                {
                    throw loadError;
                }

                if (lease == null || lease.Instance == null)
                {
                    throw new InvalidOperationException("UI loader returned no instance.");
                }

                GameObject instance = lease.Instance;

                if (instance.activeSelf || !(instance.transform is RectTransform))
                {
                    throw new InvalidOperationException("UI instance requires RectTransform and must be inactive.");
                }

                UIView view = instance.GetComponent<UIView>();

                if (view == null)
                {
                    throw new InvalidOperationException("UI prefab root must contain a UIView component.");
                }

                foreach (Canvas canvas in instance.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas.overrideSorting)
                    {
                        throw new InvalidOperationException("Window canvases cannot override layer sorting.");
                    }
                }

                entry.Driver = entry.Registration.Factory();
                var context = new UIWindowContext(
                    this,
                    entry.LifetimeCancellation.Token,
                    () => entry.VisibleCancellation?.Token ?? new CancellationToken(true),
                    () => CloseCurrentAsync(entry.Id, entry));
                entry.CreateEntered = true;
                entry.Driver.Create(context, view);
                entry.Prepared = true;
                entry.State = UIState.Hidden;
                Enqueue(() => ProcessNext(entry));
            }
            catch (Exception exception)
            {
                FailEntry(entry, exception, "load/create");
            }
        }

        private void ProcessNext(WindowEntry entry)
        {
            if (!IsCurrent(entry) || !entry.Prepared || entry.Requests.Count == 0)
            {
                return;
            }

            UIRequest request = entry.Requests[0];
            entry.Requests.RemoveAt(0);

            if (request.Cancellation.IsCancellationRequested)
            {
                request.Finish(null, cancelled: true);
                RetireUnusedLoading(entry);
            }
            else
            {
                try
                {
                    if (request.IsShow)
                    {
                        Show(entry, request);
                    }

                    entry.KeepAlive = true;
                    request.Finish(entry);
                }
                catch (Exception exception)
                {
                    Exception error = OperationError(entry, exception, "show");
                    request.Finish(null, error);
                    FailEntry(entry, error, "show");
                }
            }

            Enqueue(() => ProcessNext(entry));
        }

        private void Show(WindowEntry entry, UIRequest request)
        {
            entry.Driver.Bind(request.Args);

            if (entry.State != UIState.Visible)
            {
                entry.State = UIState.Showing;
                entry.Lease.Instance.transform.SetParent(_root.GetLayerRoot(entry.Registration.Definition.Layer), false);
                entry.VisibleCancellation = new CancellationTokenSource();
                entry.ShowEntered = true;
                entry.Lease.Instance.SetActive(true);
                entry.Driver.Show();
            }

            _layers.CommitShow(entry, request.Sequence);
            entry.State = UIState.Visible;
        }

        private void Hide(string id, TaskCompletionSource<bool> completion)
        {
            try
            {
                if (_entries.TryGetValue(id, out WindowEntry entry))
                {
                    CancelShows(entry.ReopenRequests);

                    if (entry.Retired)
                    {
                        completion.TrySetResult(true);
                        return;
                    }

                    CancelShows(entry.Requests);
                    int errorsBefore = entry.CleanupErrors.Count;
                    HideEntry(entry);

                    if (entry.CleanupErrors.Count > errorsBefore)
                    {
                        BeginClose(entry);
                        throw OperationError(entry, new AggregateException(entry.CleanupErrors), "hide");
                    }

                    RetireUnusedLoading(entry);
                }

                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private void HideEntry(WindowEntry entry)
        {
            if (!entry.ShowEntered)
            {
                return;
            }

            entry.ShowEntered = false;

            if (!entry.Retired)
            {
                entry.State = UIState.Hiding;
            }

            Capture(entry, () => entry.VisibleCancellation.Cancel());
            Capture(entry, () => RootUI.ClearSelection(entry.Lease.Instance));
            Capture(entry, () => entry.Driver.Hide());
            Capture(entry, () =>
            {
                if (entry.Lease.Instance != null)
                {
                    entry.Lease.Instance.SetActive(false);
                }
            });
            _layers.Remove(entry);
            entry.VisibleCancellation.Dispose();
            entry.VisibleCancellation = null;

            if (!entry.Retired)
            {
                entry.State = UIState.Hidden;
            }
        }

        private Task CloseCurrentAsync(string id, WindowEntry expected)
        {
            RequireThread();
            var completion = WindowEntry.NewCompletion<bool>();
            Enqueue(() =>
            {
                if (!_entries.TryGetValue(id, out WindowEntry entry) || (expected != null && entry != expected))
                {
                    completion.TrySetResult(true);
                    return;
                }

                if (expected == null)
                {
                    FailRequests(entry.ReopenRequests, null);
                }

                ForwardAsync(BeginClose(entry), completion);
            });
            return completion.Task;
        }

        private Task BeginClose(WindowEntry entry)
        {
            if (entry.CloseCompletion != null && !entry.CloseCompletion.Task.IsCompleted)
            {
                return entry.CloseCompletion.Task;
            }

            entry.CloseCompletion = WindowEntry.NewCompletion<bool>();
            entry.Retired = true;
            entry.State = UIState.Closing;
            FailRequests(entry.Requests, null);
            Capture(entry, () => entry.LoadCancellation.Cancel());
            Capture(entry, () => entry.LifetimeCancellation.Cancel());
            HideEntry(entry);

            if (entry.CreateEntered && !entry.ReleasedDriver)
            {
                entry.ReleasedDriver = true;
                Capture(entry, () => entry.Driver.Release());
            }

            ReleaseEntryAsync(entry);
            return entry.CloseCompletion.Task;
        }

        private async void ReleaseEntryAsync(WindowEntry entry)
        {
            try
            {
                await entry.LoadFinished.Task;

                if (entry.Lease != null)
                {
                    await entry.Lease.ReleaseAsync();
                }

                Enqueue(() => CompleteClose(entry));
            }
            catch (Exception exception)
            {
                Enqueue(() =>
                {
                    Exception error = OperationError(entry, exception, "release");
                    FailRequests(entry.ReopenRequests, error);
                    entry.CloseCompletion.TrySetException(error);
                    _ = entry.CloseCompletion.Task.Exception;
                });
            }
        }

        private void CompleteClose(WindowEntry entry)
        {
            _entries.Remove(entry.Id);
            entry.State = UIState.Closed;
            entry.Prepared = false;
            entry.Driver = null;
            entry.Lease = null;
            entry.LoadCancellation.Dispose();
            entry.LifetimeCancellation.Dispose();
            var reopen = new List<UIRequest>(entry.ReopenRequests);
            entry.ReopenRequests.Clear();

            if (entry.CleanupErrors.Count == 0)
            {
                entry.CloseCompletion.TrySetResult(true);

                foreach (UIRequest request in reopen)
                {
                    Enqueue(() => Accept(request));
                }
            }
            else
            {
                Exception error = OperationError(entry, new AggregateException(entry.CleanupErrors), "close");
                entry.CloseCompletion.TrySetException(error);
                _ = entry.CloseCompletion.Task.Exception;
                FailRequests(reopen, error);
            }
        }

        private void CancelRequest(UIRequest request)
        {
            if (request.Completion.Task.IsCompleted)
            {
                return;
            }

            WindowEntry entry = request.Entry;

            if (entry != null)
            {
                entry.Requests.Remove(request);
                entry.ReopenRequests.Remove(request);
            }

            request.Finish(null, cancelled: true);

            if (entry != null)
            {
                RetireUnusedLoading(entry);
            }
        }

        private void RetireUnusedLoading(WindowEntry entry)
        {
            if (!entry.Retired && !entry.KeepAlive && entry.Requests.Count == 0)
            {
                BeginClose(entry);
            }
        }

        private void FailEntry(WindowEntry entry, Exception exception, string stage)
        {
            FailRequests(entry.Requests, OperationError(entry, exception, stage));
            BeginClose(entry);
        }

        private static Exception OperationError(WindowEntry entry, Exception exception, string stage)
        {
            return new InvalidOperationException(
                $"UI '{entry.Id}' ({entry.Registration.Definition.AssetKey}), generation {entry.Generation}, stage {stage} failed.",
                exception);
        }

        private static void FailRequests(List<UIRequest> requests, Exception error)
        {
            foreach (UIRequest request in requests)
            {
                request.Finish(null, error, error == null);
            }

            requests.Clear();
        }

        private static void CancelShows(List<UIRequest> requests)
        {
            for (int index = requests.Count - 1; index >= 0; index--)
            {
                UIRequest request = requests[index];

                if (request.IsShow)
                {
                    requests.RemoveAt(index);
                    request.Finish(null, cancelled: true);
                }
            }
        }

        private static void Capture(WindowEntry entry, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                entry.CleanupErrors.Add(exception);
            }
        }

        private bool IsCurrent(WindowEntry entry)
        {
            return !entry.Retired && _entries.TryGetValue(entry.Id, out WindowEntry current) && current == entry;
        }

        private void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
            {
                throw new InvalidOperationException("UIService APIs must be called on their Unity main thread.");
            }
        }

        private void Enqueue(Action action)
        {
            RequireThread();
            _commands.Enqueue(action);

            if (_draining)
            {
                return;
            }

            _draining = true;

            try
            {
                while (_commands.Count > 0)
                {
                    _commands.Dequeue()();
                }
            }
            finally
            {
                _draining = false;
            }
        }

        private static async void ForwardAsync(Task task, TaskCompletionSource<bool> completion)
        {
            try
            {
                await task;
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }
    }
}
