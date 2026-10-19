using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Editor
{
    [CustomEditor(typeof(CharacterAttributeAsset))]
    public sealed class CharacterAttributeEditor : UnityEditor.Editor
    {
        private bool m_ShowVersion;
        private string m_Message;
        private bool m_Valid;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ConfigurationId"), new GUIContent("角色配置 ID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Attributes"), new GUIContent("属性"), true);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("可产生标记", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("这里只声明该角色可以产生哪些标记，不会自动挂到自己身上。通用标记也需要显式选择。", MessageType.Info);
            SerializedProperty catalogProperty = serializedObject.FindProperty("MarkerCatalog");
            EditorGUILayout.PropertyField(catalogProperty, new GUIContent("标记定义库"));
            MarkerCatalogAsset catalog = catalogProperty.objectReferenceValue as MarkerCatalogAsset;
            EffectMarkerOptions options = new EffectMarkerOptions(catalog, null, false);
            SerializedProperty selected = serializedObject.FindProperty("ProducedMarkerIds");
            for (int i = 0; i < selected.arraySize; i++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    MarkerAuthoringGUI.MarkerPicker(selected.GetArrayElementAtIndex(i), options, catalog);
                    if (MarkerAuthoringGUI.RemoveButton(selected, i))
                    {
                        break;
                    }
                }
            }

            using (new EditorGUI.DisabledScope(catalog == null))
            {
                if (GUILayout.Button("添加可产生标记"))
                {
                    MarkerAuthoringGUI.AppendId(selected);
                }
            }

            MarkerAuthoringGUI.Version(serializedObject, ref m_ShowVersion);
            if (serializedObject.ApplyModifiedProperties())
            {
                m_Message = null;
            }

            if (GUILayout.Button("校验可产生标记"))
            {
                m_Valid = MarkerAuthoringValidation.TryValidateProfile((CharacterAttributeAsset)target, out var issues);
                m_Message = m_Valid ? "角色标记声明有效；运行时产生资格校验将在标记系统接入时实现。" : string.Join("\n", issues);
            }

            if (m_Message != null)
            {
                EditorGUILayout.HelpBox(m_Message, m_Valid ? MessageType.Info : MessageType.Error);
            }
        }
    }
}
