using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Editor
{
    [CustomEditor(typeof(VersionedConfigurationAsset), true)]
    public sealed class ConfigurationAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("稳定 ID 由项目显式分配。数值使用最多四位小数的十进制文本；此处只编辑和校验配置，不创建运行会话。", MessageType.Info);
            if (target is EffectGraphAsset graph)
            {
                EditorGUILayout.HelpBox("Effect 不绑定角色。请在节点编辑器选择角色配置上下文，并通过属性下拉框和 port 编辑。", MessageType.Info);
                if (GUILayout.Button("打开效果节点编辑器"))
                {
                    EffectGraphWindow.Open(graph);
                }
            }
            else
            {
                DrawDefaultInspector();
            }

            if (target is AttributeCatalogAsset catalog)
            {
                if (GUILayout.Button("校验通用属性库"))
                {
                    bool valid = ConfigurationValidator.TryValidateCatalog(catalog, out var issues);
                    EditorUtility.DisplayDialog("通用属性库", valid ? $"属性库配置有效，共 {catalog.Attributes.Length} 项。" : string.Join("\n", issues), "确定");
                }
            }

        }
    }
}
