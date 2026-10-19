using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Editor
{
    public sealed partial class EffectGraphWindow
    {
        private EffectMarkerOptions m_AddMarkerOptions;
        private EffectMarkerOptions m_RemoveMarkerOptions;
        private string m_MarkerValidation;
        private bool m_MarkerValid;

        private void DrawMarkerOperations()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("标记操作 · 配置草稿", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("这里只保存向独立标记系统发出的请求。运行时尚未接入：操作不会执行，含操作的效果不能编译为可运行会话。", MessageType.Warning);
            SerializedProperty catalogProperty = m_Serialized.FindProperty("MarkerCatalog");
            EditorGUILayout.PropertyField(catalogProperty, new GUIContent("标记定义库"));
            MarkerCatalogAsset catalog = catalogProperty.objectReferenceValue as MarkerCatalogAsset;
            if (m_SourcePreview != null && m_SourcePreview.MarkerCatalog != null && catalog != m_SourcePreview.MarkerCatalog)
            {
                if (GUILayout.Button("使用施加者预览角色的标记库"))
                {
                    catalogProperty.objectReferenceValue = m_SourcePreview.MarkerCatalog;
                }
            }

            SerializedProperty operations = m_Serialized.FindProperty("MarkerOperations");
            for (int i = 0; i < operations.arraySize; i++)
            {
                SerializedProperty operation = operations.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"操作 {i + 1}", EditorStyles.boldLabel);
                        using (new EditorGUI.DisabledScope(i == 0))
                        {
                            if (GUILayout.Button("上移", GUILayout.Width(45)))
                            {
                                operations.MoveArrayElement(i, i - 1);
                                break;
                            }
                        }

                        if (MarkerAuthoringGUI.RemoveButton(operations, i))
                        {
                            break;
                        }
                    }

                    Choice(operation, "Timing", "执行时机", s_MarkerTimings);
                    Choice(operation, "Kind", "操作", s_MarkerKinds);
                    Choice(operation, "Target", "接收对象", s_MarkerTargets);
                    bool adding = operation.FindPropertyRelative("Kind").intValue == (int)MarkerOperationKind.Add;
                    MarkerAuthoringGUI.MarkerPicker(operation.FindPropertyRelative("MarkerId"),
                        adding ? m_AddMarkerOptions : m_RemoveMarkerOptions, catalog);
                    if (!adding)
                    {
                        Choice(operation, "RemovalScope", "移除范围", s_RemovalScopes);
                        Choice(operation, "RemovalMode", "移除方式", s_RemovalModes);
                    }

                    if (adding || operation.FindPropertyRelative("RemovalMode").intValue == (int)MarkerRemovalMode.Count)
                    {
                        EditorGUILayout.PropertyField(operation.FindPropertyRelative("Count"), new GUIContent("请求层数"));
                    }
                }
            }

            if (GUILayout.Button("添加标记操作"))
            {
                int index = operations.arraySize;
                operations.InsertArrayElementAtIndex(index);
                SerializedProperty operation = operations.GetArrayElementAtIndex(index);
                operation.FindPropertyRelative("Kind").intValue = (int)MarkerOperationKind.Add;
                operation.FindPropertyRelative("Timing").intValue = (int)MarkerOperationTiming.Applied;
                operation.FindPropertyRelative("Target").intValue = (int)MarkerOperationTarget.Target;
                operation.FindPropertyRelative("MarkerId").longValue = 0;
                operation.FindPropertyRelative("Count").intValue = 1;
                operation.FindPropertyRelative("RemovalScope").intValue = (int)MarkerRemovalScope.AllSources;
                operation.FindPropertyRelative("RemovalMode").intValue = (int)MarkerRemovalMode.Count;
            }

            if (GUILayout.Button("校验标记草稿"))
            {
                m_Serialized.ApplyModifiedProperties();
                m_MarkerValid = MarkerAuthoringValidation.TryValidateEffect(m_Graph, m_SourcePreview, out var issues);
                m_MarkerValidation = m_MarkerValid ? "配置草稿有效；尚未实现标记请求执行，不能用于运行会话。" : string.Join("\n", issues);
                Invalidate();
            }

            if (m_MarkerValidation != null)
            {
                EditorGUILayout.HelpBox(m_MarkerValidation, m_MarkerValid ? MessageType.Info : MessageType.Error);
            }

        }

        private static void Choice(SerializedProperty operation, string field, string label, string[] labels)
        {
            SerializedProperty property = operation.FindPropertyRelative(field);
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(label, property.intValue, labels);
            if (EditorGUI.EndChangeCheck())
            {
                property.intValue = selected;
            }
        }

        private static readonly string[] s_MarkerTimings = { "施加时", "周期执行时" };
        private static readonly string[] s_MarkerKinds = { "增加", "移除" };
        private static readonly string[] s_MarkerTargets = { "效果目标", "效果来源" };
        private static readonly string[] s_RemovalScopes = { "全部来源", "本次效果的来源" };
        private static readonly string[] s_RemovalModes = { "指定层数", "全部" };
    }
}
