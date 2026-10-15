using System;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Logging.Editor
{
    internal static class ConsoleToolbarExtension
    {
        private static IDisposable s_Registration;
        private static double s_NextRefresh;

        internal static void Register()
        {
            Unregister();
            ConsoleBridge.TryRegisterToolbar(Draw, out s_Registration);
        }

        internal static void Unregister()
        {
            s_Registration?.Dispose();
            s_Registration = null;
        }

        private static void Draw()
        {
            var controller = ConsoleFilterController.Shared;
            if (EditorApplication.timeSinceStartup >= s_NextRefresh)
            {
                s_NextRefresh = EditorApplication.timeSinceStartup + 0.2;
                controller.RefreshFromConsole();
            }
            using (new GUILayout.HorizontalScope(GUILayout.MaxWidth(240)))
            {
                ConsoleFilterWindow.DrawCategoryButton(controller);
                if (GUILayout.Button("筛选", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    ConsoleFilterWindow.Open();
                }
            }
        }
    }
}
