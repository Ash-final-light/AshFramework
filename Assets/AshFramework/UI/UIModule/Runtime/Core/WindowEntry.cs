using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AshFramework.UI.UIModule
{
    internal sealed class WindowEntry : IUIHandle
    {
        public string Id { get; }
        public UIState State { get; set; }
        public readonly long Generation;
        public readonly UIRegistry.Registration Registration;
        public readonly List<UIRequest> Requests = new List<UIRequest>();
        public readonly List<UIRequest> ReopenRequests = new List<UIRequest>();
        public readonly List<Exception> CleanupErrors = new List<Exception>();
        public readonly CancellationTokenSource LoadCancellation = new CancellationTokenSource();
        public readonly CancellationTokenSource LifetimeCancellation = new CancellationTokenSource();
        public readonly TaskCompletionSource<bool> LoadFinished = NewCompletion<bool>();
        public CancellationTokenSource VisibleCancellation;
        public TaskCompletionSource<bool> CloseCompletion;
        public IUIViewLease Lease;
        public IWindowDriver Driver;
        public bool Retired;
        public bool Prepared;
        public bool KeepAlive;
        public bool CreateEntered;
        public bool ShowEntered;
        public bool ReleasedDriver;
        public long ShowSequence;

        public WindowEntry(string id, long generation, UIRegistry.Registration registration)
        {
            Id = id;
            Generation = generation;
            Registration = registration;
            State = UIState.Loading;
        }

        public static TaskCompletionSource<T> NewCompletion<T>()
        {
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    internal sealed class UIRequest
    {
        public readonly string Id;
        public readonly UIRegistry.Registration Registration;
        public readonly bool IsShow;
        public readonly long Sequence;
        public readonly CancellationToken Cancellation;
        public readonly TaskCompletionSource<IUIHandle> Completion = WindowEntry.NewCompletion<IUIHandle>();
        public object Args;
        public WindowEntry Entry;
        public CancellationTokenRegistration CancellationRegistration;

        public UIRequest(
            string id,
            UIRegistry.Registration registration,
            bool isShow,
            object args,
            long sequence,
            CancellationToken cancellation)
        {
            Id = id;
            Registration = registration;
            IsShow = isShow;
            Args = args;
            Sequence = sequence;
            Cancellation = cancellation;
        }

        public void Finish(IUIHandle handle, Exception error = null, bool cancelled = false)
        {
            CancellationRegistration.Dispose();
            Args = null;
            Entry = null;

            if (cancelled)
            {
                Completion.TrySetCanceled();
            }
            else if (error != null)
            {
                Completion.TrySetException(error);
            }
            else
            {
                Completion.TrySetResult(handle);
            }
        }
    }
}
