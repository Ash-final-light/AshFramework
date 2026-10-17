using System;
using UnityEditor;
using UnityEngine;

namespace AshFramework.UI.UIModule.Editor
{
    public sealed class UIWindowGeneratorWindow : EditorWindow
    {
        [SerializeField] private UIWindowGenerationOptions _options = new UIWindowGenerationOptions();
        [SerializeField] private string _status;

        [MenuItem("Tools/AshFramework/UIModule/生成 Window 基类")]
        public static void Open()
        {
            var window = GetWindow<UIWindowGeneratorWindow>("Window 基类生成器");
            window.minSize = new Vector2(540f, 340f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Window 生命周期骨架", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("窗口名（不带 Window）", _options.WindowName);

            if (EditorGUI.EndChangeCheck())
            {
                string oldName = _options.WindowName;
                _options.WindowName = name;

                if (_options.ScriptDirectory == "Assets/GameScripts/UI/" + oldName)
                {
                    _options.ScriptDirectory = "Assets/GameScripts/UI/" + name;
                }

                if (_options.Namespace == "Game.UI." + oldName)
                {
                    _options.Namespace = "Game.UI." + name;
                }

                if (_options.PrefabPath == "Assets/Resources/UI/" + oldName + ".prefab")
                {
                    _options.PrefabPath = "Assets/Resources/UI/" + name + ".prefab";
                }
            }

            _options.Namespace = EditorGUILayout.TextField("命名空间", _options.Namespace);
            EditorGUILayout.BeginHorizontal();
            _options.ScriptDirectory = EditorGUILayout.TextField("脚本目录", _options.ScriptDirectory);

            if (GUILayout.Button("选择", GUILayout.Width(48f)))
            {
                string folder = EditorUtility.OpenFolderPanel("选择 Assets 下的脚本目录", Application.dataPath, "");

                if (!string.IsNullOrEmpty(folder))
                {
                    _options.ScriptDirectory = FileUtil.GetProjectRelativePath(folder);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _options.PrefabPath = EditorGUILayout.TextField("计划 Prefab 路径", _options.PrefabPath);

            if (GUILayout.Button("选择", GUILayout.Width(48f)))
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "选择计划 Prefab 路径", _options.WindowName, "prefab", "这里只记录加载地址，不创建或覆盖 Prefab。");

                if (!string.IsNullOrEmpty(path))
                {
                    _options.PrefabPath = path;
                }
            }

            EditorGUILayout.EndHorizontal();
            _options.Layer = (UILayer)EditorGUILayout.EnumPopup("显示层级", _options.Layer);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "更新 Generated/*WindowBase.g.cs；Window.cs、Args.cs、View.cs 只在不存在时创建。\n" +
                "不自动扫描控件，不创建 Prefab，不移动现有代码。生成后将对应 View 挂到窗口 Prefab 根节点。",
                MessageType.Info);

            string error = UIWindowCodeGenerator.GetValidationError(_options);

            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(error != null || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating))
            {
                if (GUILayout.Button("生成 / 更新基类", GUILayout.Height(30f)))
                {
                    Generate();
                }
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }
        }

        private void Generate()
        {
            try
            {
                UIWindowGenerationResult result = UIWindowCodeGenerator.Generate(_options);
                AssetDatabase.Refresh();
                _status = $"已写入 {result.Written.Count} 个文件，保留 {result.Preserved.Count} 个已有业务文件。";
                string scriptPath = _options.ScriptDirectory.TrimEnd('/', '\\') + "/" + _options.WindowName + "Window.cs";
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath));
            }
            catch (Exception exception)
            {
                _status = exception.Message;
                Debug.LogException(exception);
            }
        }
    }
}
