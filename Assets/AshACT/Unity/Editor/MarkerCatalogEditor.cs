using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Editor
{
    [CustomEditor(typeof(MarkerCatalogAsset))]
    public sealed class MarkerCatalogEditor : UnityEditor.Editor
    {
        private bool m_ShowVersion;
        private string m_Message;
        private bool m_Valid;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("标记定义库", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("在这里统一维护标记名称和说明。分组只用于展示；角色需要显式选择可产生标记。层数、计时和叠加刷新规则将在后续实现。", MessageType.Info);
            SerializedProperty entries = serializedObject.FindProperty("Markers");
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"定义 {i + 1}", EditorStyles.boldLabel);
                        if (MarkerAuthoringGUI.RemoveButton(entries, i))
                        {
                            break;
                        }
                    }

                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("MarkerId"), new GUIContent("稳定 ID"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("DisplayName"), new GUIContent("名称"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Group"), new GUIContent("分组"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Description"), new GUIContent("说明"));
                }
            }

            if (GUILayout.Button("添加标记定义"))
            {
                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("MarkerId").longValue = 0;
                entry.FindPropertyRelative("DisplayName").stringValue = string.Empty;
                entry.FindPropertyRelative("Group").stringValue = string.Empty;
                entry.FindPropertyRelative("Description").stringValue = string.Empty;
            }

            MarkerAuthoringGUI.Version(serializedObject, ref m_ShowVersion);
            if (serializedObject.ApplyModifiedProperties())
            {
                m_Message = null;
            }

            if (GUILayout.Button("校验标记定义"))
            {
                m_Valid = MarkerAuthoringValidation.TryValidateCatalog((MarkerCatalogAsset)target, out var issues);
                m_Message = m_Valid ? "标记定义有效。这里只校验配置，不创建运行状态。" : string.Join("\n", issues);
            }

            if (m_Message != null)
            {
                EditorGUILayout.HelpBox(m_Message, m_Valid ? MessageType.Info : MessageType.Error);
            }
        }
    }
}
