using System;
using System.Threading.Tasks;
using UnityEngine;

namespace AshFramework.UI.UIModule.Examples.StackDemo
{
    public static class StackExampleActions
    {
        // Event handlers cannot return Task to Unity; observe every asynchronous failure here.
        public static async void Observe(Task operation, Action completed = null)
        {
            try
            {
                await operation;
                completed?.Invoke();
            }
            catch (OperationCanceledException)
            {
                // Expected when the example scene is leaving or an opening window is closed.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
