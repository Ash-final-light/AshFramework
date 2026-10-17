using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AshFramework.UI.UIModule
{
    public interface IUIViewLoader
    {
        Task<IUIViewLease> InstantiateAsync(
            string assetKey,
            Transform inactiveStagingRoot,
            CancellationToken cancellationToken);
    }

    public interface IUIViewLease
    {
        GameObject Instance { get; }

        // Idempotent. A failed release must permit retry of unfinished cleanup.
        Task ReleaseAsync();
    }
}
