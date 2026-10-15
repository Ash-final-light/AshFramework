using UnityEditor;

namespace AshFramework.Logging.Editor
{
    [InitializeOnLoad]
    internal static class LogEditorLifecycle
    {
        static LogEditorLifecycle()
        {
            LogSystem.BindMainThread();
            EditorApplication.update += Pump;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.projectChanged += LogSettingsEditor.Invalidate;
            EditorApplication.quitting += Close;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            ConsoleToolbarExtension.Register();
        }

        private static void Pump()
        {
            if (!EditorApplication.isPlaying)
            {
                LogSystem.Pump();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Close();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                LogSystem.ResumeFallback();
            }
        }

        private static void Close()
        {
            LogSystem.Shutdown();
        }

        private static void BeforeReload()
        {
            Close();
            ConsoleToolbarExtension.Unregister();
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.projectChanged -= LogSettingsEditor.Invalidate;
            EditorApplication.quitting -= Close;
        }
    }
}
