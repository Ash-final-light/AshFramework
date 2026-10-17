using UnityEditor;
using UnityEngine;

namespace AshFramework.UI.UIModule.Editor
{
    [CustomEditor(typeof(RootUI))]
    public sealed class RootUIInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            if (GUILayout.Button("生成 Window 基类"))
            {
                UIWindowGeneratorWindow.Open();
            }

            EditorGUILayout.HelpBox(
                "生成生命周期骨架；业务脚本仅首次创建，只有带生成器标记的 WindowBase.g.cs 可以更新。",
                MessageType.Info);
        }

        [MenuItem("CONTEXT/RootUI/AshFramework/UIModule/生成 Window 基类")]
        private static void GenerateFromContext(MenuCommand command)
        {
            UIWindowGeneratorWindow.Open();
        }
    }
}
