using UnityEditor;
using UnityEngine;

namespace AshFramework.Logging.Editor
{
    internal sealed class ConsoleFilterWindow : EditorWindow
    {
        private double m_NextRefresh;

        [MenuItem("Tools/AshFramework/Logging/Console Filter")]
        internal static void Open()
        {
            var window = GetWindow<ConsoleFilterWindow>("Ash Log Filter");
            window.minSize = new Vector2(330f, 165f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += Refresh;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Refresh;
        }

        private void Refresh()
        {
            if (EditorApplication.timeSinceStartup < m_NextRefresh)
            {
                return;
            }
            m_NextRefresh = EditorApplication.timeSinceStartup + 0.2;
            var controller = ConsoleFilterController.Shared;
            string previous = controller.CurrentSearch;
            var previousMode = controller.State.Mode;
            controller.RefreshFromConsole();
            if (previous != controller.CurrentSearch || previousMode != controller.State.Mode)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            var controller = ConsoleFilterController.Shared;
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCategoryButton(controller);
                if (GUILayout.Button("打开 Console", GUILayout.Width(110)))
                {
                    ConsoleBridge.OpenConsole();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全部 Console 日志"))
                {
                    controller.ShowAll();
                }
                using (new EditorGUI.DisabledScope(!controller.State.OwnsSearch))
                {
                    if (GUILayout.Button("恢复原搜索"))
                    {
                        controller.RestorePrevious();
                    }
                }
                if (GUILayout.Button("设置", GUILayout.Width(60)))
                {
                    LogSettingsEditor.OpenSettings();
                }
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("当前模式", ModeLabel(controller));
            EditorGUILayout.LabelField("原生搜索", controller.CurrentSearch.Length == 0 ? "（空）" : controller.CurrentSearch);
            if (GUILayout.Button("复制当前分类搜索词"))
            {
                EditorGUIUtility.systemCopyBuffer = controller.State.Mode == ConsoleFilterMode.Category
                    ? LogFormatter.CategorySearch(controller.State.SelectedCategory) : LogFormatter.FrameworkSearch;
            }

            var required = ConsoleCapabilities.ReadSearch | ConsoleCapabilities.WriteSearch;
            if ((ConsoleBridge.Capabilities & required) != required)
            {
                EditorGUILayout.HelpBox("当前 Unity 的搜索适配不可用。分类菜单中可复制搜索词，粘贴到原生 Console 使用。" +
                    ConsoleBridge.FailureReason, MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("分类筛选复用 Console 搜索框。手动输入关键词后切换为手动模式；筛选不影响文件记录。", MessageType.Info);
            }
            if (!string.IsNullOrEmpty(controller.LastError))
            {
                EditorGUILayout.HelpBox(controller.LastError, MessageType.Warning);
            }
        }

        internal static void DrawCategoryButton(ConsoleFilterController controller)
        {
            if (!GUILayout.Button("分类：" + ModeLabel(controller) + " ▾", EditorStyles.popup))
            {
                return;
            }
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("全部框架日志"), controller.State.Mode == ConsoleFilterMode.Framework,
                () => controller.ShowFramework());
            menu.AddSeparator("");
            foreach (string category in LogCategoryRegistry.GetCategories())
            {
                string captured = category;
                menu.AddItem(new GUIContent(category), controller.State.Mode == ConsoleFilterMode.Category &&
                    controller.State.SelectedCategory == category, () => controller.ApplyCategory(captured));
            }
            menu.AddSeparator("");
            foreach (string category in LogCategoryRegistry.GetCategories())
            {
                string captured = category;
                menu.AddItem(new GUIContent("复制搜索词/" + category), false,
                    () => EditorGUIUtility.systemCopyBuffer = LogFormatter.CategorySearch(captured));
            }
            menu.ShowAsContext();
        }

        private static string ModeLabel(ConsoleFilterController controller)
        {
            switch (controller.State.Mode)
            {
                case ConsoleFilterMode.Category:
                    return controller.State.SelectedCategory;
                case ConsoleFilterMode.Framework:
                    return "全部框架日志";
                case ConsoleFilterMode.Manual:
                    return "手动搜索";
                default:
                    return "全部 Console 日志";
            }
        }
    }
}
