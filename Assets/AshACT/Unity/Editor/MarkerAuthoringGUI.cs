using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Editor
{
    internal static class MarkerAuthoringGUI
    {
        internal static void Version(SerializedObject serialized, ref bool expanded)
        {
            expanded = EditorGUILayout.Foldout(expanded, "高级：配置版本", true);
            if (expanded)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(serialized.FindProperty("SchemaVersion"), new GUIContent("结构版本"));
                }

                EditorGUILayout.PropertyField(serialized.FindProperty("ReleaseVersion"), new GUIContent("发布版本"));
            }
        }

        internal static void MarkerPicker(SerializedProperty property, EffectMarkerOptions options, MarkerCatalogAsset catalog)
        {
            uint id = (uint)property.longValue;
            int current = options.IndexOf(id) + 1;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("标记", current, options.Labels);
            if (EditorGUI.EndChangeCheck() && selected > 0)
            {
                property.longValue = options.At(selected);
                id = (uint)property.longValue;
                current = selected;
            }

            if (current == 0)
            {
                EditorGUILayout.HelpBox($"标记 #{id} 不在当前可选范围内。原编号保留，请检查标记库和来源角色。", MessageType.Warning);
            }

            if (catalog != null && catalog.Markers != null)
            {
                foreach (MarkerDefinitionEntry entry in catalog.Markers)
                {
                    if (entry != null && entry.MarkerId == id && !string.IsNullOrWhiteSpace(entry.Description))
                    {
                        EditorGUILayout.LabelField(entry.Description, EditorStyles.wordWrappedMiniLabel);
                        break;
                    }
                }
            }
        }

        internal static void AppendId(SerializedProperty list)
        {
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).longValue = 0;
        }

        internal static bool RemoveButton(SerializedProperty list, int index)
        {
            if (GUILayout.Button("删除", GUILayout.Width(50)))
            {
                list.DeleteArrayElementAtIndex(index);
                return true;
            }

            return false;
        }
    }
}
