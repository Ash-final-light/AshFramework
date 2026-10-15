using System.IO;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Logging.Editor
{
    [CustomEditor(typeof(LogSettings))]
    internal sealed class LogSettingsEditor : UnityEditor.Editor
    {
        internal const string SettingsPath = "Assets/AshFramework/Log/Resources/AshLogSettings.asset";
        private static bool s_Searched;
        private static LogSettings s_Settings;

        internal static LogSettings FindSettings()
        {
            if (!s_Searched)
            {
                s_Settings = AssetDatabase.LoadAssetAtPath<LogSettings>(SettingsPath);
                s_Searched = true;
            }
            return s_Settings;
        }

        internal static void Invalidate()
        {
            s_Searched = false;
            s_Settings = null;
            LogCategoryRegistry.Invalidate();
        }

        [MenuItem("Tools/AshFramework/Logging/Settings")]
        internal static void OpenSettings()
        {
            var settings = FindSettings();
            if (settings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                AssetDatabase.Refresh();
                settings = CreateInstance<LogSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                Undo.RegisterCreatedObjectUndo(settings, "Create Ash Log Settings");
                AssetDatabase.SaveAssets();
                Invalidate();
            }
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("此资源在播放/Player 启动时读取。运行中的配置使用初始化快照，修改在下次初始化生效。", MessageType.Info);
            if (DrawDefaultInspector())
            {
                LogCategoryRegistry.Invalidate();
            }
            var settings = (LogSettings)target;
            if (settings.CustomCategories != null)
            {
                foreach (string category in settings.CustomCategories)
                {
                    if (!LogCategories.IsValid(category))
                    {
                        EditorGUILayout.HelpBox("无效分类不会出现在菜单。请使用 1～64 个英文字母、数字、_、.、-。", MessageType.Warning);
                        break;
                    }
                }
            }
            if (GUILayout.Button("保存设置"))
            {
                AssetDatabase.SaveAssets();
            }
        }
    }
}
