using UnityEngine;

namespace AshFramework.Logging
{
    [AddComponentMenu("")]
    internal sealed class LogRuntimeDriver : MonoBehaviour
    {
        private static LogRuntimeDriver s_Instance;

        internal static void EnsureCreated()
        {
            if (s_Instance != null)
            {
                return;
            }
            var owner = new GameObject("[Ash Log]") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(owner);
            s_Instance = owner.AddComponent<LogRuntimeDriver>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // 没有设置资源时只启用最小 Console 回退，业务可以自行 Initialize。
            if (!LogSystem.IsInitialized)
            {
                var settings = Resources.Load<LogSettings>(LogSettings.ResourceName);
                if (settings != null)
                {
                    LogSystem.Initialize(settings.CreateConfig());
                }
            }
            EnsureCreated();
        }

        private void Update()
        {
            LogSystem.Pump();
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }
    }
}
