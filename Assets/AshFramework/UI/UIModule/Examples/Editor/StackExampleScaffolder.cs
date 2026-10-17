using System;
using System.IO;
using AshFramework.UI.UIModule.Editor;
using UnityEditor;
using UnityEngine;

namespace AshFramework.UI.UIModule.Examples.Editor
{
    public static class StackExampleScaffolder
    {
        public const string ExampleRoot = "Assets/AshFramework/UI/UIModule/Examples";
        public const string ScenePath = ExampleRoot + "/Scenes/StackDemo.unity";
        public const string ResourceFolder = ExampleRoot + "/Resources/UIModuleExamples/StackDemo";
        public const string BusinessNamespace = "AshFramework.UI.UIModule.Examples.StackDemo";

        [MenuItem("Tools/AshFramework/UIModule/Examples/1. Generate Window Scripts")]
        public static void GenerateScripts()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before generating example code.");
            }

            int written = 0;
            int preserved = 0;

            foreach (string name in new[] { "StackFirst", "StackSecond" })
            {
                UIWindowGenerationResult result = UIWindowCodeGenerator.Generate(new UIWindowGenerationOptions
                {
                    WindowName = name,
                    Namespace = BusinessNamespace,
                    ScriptDirectory = ExampleRoot + "/Runtime/Windows/" + name,
                    PrefabPath = ResourceFolder + "/" + name + ".prefab",
                    Layer = UILayer.Popup
                });
                written += result.Written.Count;
                preserved += result.Preserved.Count;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[StackDemo] Standard generator: {written} files written, {preserved} business files preserved.");
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
