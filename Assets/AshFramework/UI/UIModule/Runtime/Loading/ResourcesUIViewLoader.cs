using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AshFramework.UI.UIModule
{
    public sealed class ResourcesUIViewLoader : IUIViewLoader
    {
        public async Task<IUIViewLease> InstantiateAsync(
            string assetKey,
            Transform inactiveStagingRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (inactiveStagingRoot == null || inactiveStagingRoot.gameObject.activeInHierarchy)
            {
                throw new ArgumentException("An inactive staging root is required.", nameof(inactiveStagingRoot));
            }

            ResourceRequest request = Resources.LoadAsync<GameObject>(assetKey);

            // Resources requests cannot be aborted. Do not instantiate a cancelled result.
            while (!request.isDone)
            {
                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (inactiveStagingRoot == null)
            {
                throw new OperationCanceledException("UI root was destroyed.");
            }

            GameObject prefab = request.asset as GameObject;

            if (prefab == null)
            {
                throw new InvalidOperationException($"UI prefab not found in Resources: {assetKey}");
            }

            GameObject instance = Object.Instantiate(prefab, inactiveStagingRoot, false);
            instance.SetActive(false);
            return new ResourcesUIViewLease(instance);
        }

        private sealed class ResourcesUIViewLease : IUIViewLease
        {
            private GameObject _instance;
            private Task _release;

            public GameObject Instance => _instance;

            public ResourcesUIViewLease(GameObject instance)
            {
                _instance = instance;
            }

            public Task ReleaseAsync()
            {
                if (_release == null || _release.IsFaulted || _release.IsCanceled)
                {
                    _release = ReleaseCoreAsync();
                }

                return _release;
            }

            private async Task ReleaseCoreAsync()
            {
                if (_instance != null)
                {
                    if (Application.isPlaying)
                    {
                        Object.Destroy(_instance);

                        while (_instance != null)
                        {
                            await Task.Yield();
                        }
                    }
                    else
                    {
                        Object.DestroyImmediate(_instance);
                    }
                }

                _instance = null;
            }
        }
    }
}
