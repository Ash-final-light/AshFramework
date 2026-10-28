using AshFramework.Collide.Unity;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Collide.Editor
{
    [CustomEditor(typeof(CollisionBodyBehaviour))]
    [CanEditMultipleObjects]
    public sealed class CollisionBodyBehaviourEditor : UnityEditor.Editor
    {
        private void OnEnable()
        {
            Undo.undoRedoPerformed += SynchronizeBindings;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= SynchronizeBindings;
        }

        private void SynchronizeBindings()
        {
            foreach (Object item in targets)
            {
                if (item is CollisionBodyBehaviour body && body != null)
                {
                    body.BindWorld(body.World);
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_world"), new GUIContent("显式绑定世界"));
            DrawPropertiesExcluding(serializedObject, "m_Script", "_world");
            serializedObject.ApplyModifiedProperties();
            SynchronizeBindings();
            if (targets.Length == 1)
            {
                CollisionBodyBehaviour body = (CollisionBodyBehaviour)target;
                if (body.World != null && body.World.TryGetBodyId(body, out CollisionBodyId id))
                {
                    EditorGUILayout.LabelField("世界内 ID", id.ToString());
                    EditorGUILayout.LabelField("参与检测", body.World.IsBodyActive(body) ? "是（最近提交状态）" : "暂停 / 尚未提交");
                    string error = body.World.GetBodyError(body);
                    if (!string.IsNullOrEmpty(error))
                    {
                        EditorGUILayout.HelpBox(error, MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("显式绑定并初始化世界后分配核心 ID；几何在世界 Step 时统一提交。", MessageType.Info);
                }
            }
        }
    }
}
