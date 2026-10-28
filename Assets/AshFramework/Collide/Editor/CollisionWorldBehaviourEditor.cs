using System.Collections.Generic;
using AshFramework.Collide.Unity;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Collide.Editor
{
    [CustomEditor(typeof(CollisionWorldBehaviour))]
    public sealed class CollisionWorldBehaviourEditor : UnityEditor.Editor
    {
        private readonly List<CollisionBodyBehaviour> _mismatches = new List<CollisionBodyBehaviour>();
        private string _checkMessage;
        private bool _checkPassed;

        public override void OnInspectorGUI()
        {
            CollisionWorldBehaviour world = (CollisionWorldBehaviour)target;
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(world.IsInitialized))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_plane"), new GUIContent("统一碰撞平面"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_boundsCenter"), new GUIContent("固定范围中心"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_boundsHalfExtents"), new GUIContent("固定范围半尺寸"));
            }

            if (EditorGUI.EndChangeCheck())
            {
                _checkMessage = null;
                _mismatches.Clear();
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("范围使用绝对平面坐标，第二维对应 Y 或 Z，与 World 的 Transform 无关。初始化后配置固定。", MessageType.Info);
            EditorGUILayout.LabelField("世界状态", world.IsInitialized ? "已初始化，等待显式 Step" : "未初始化，需由代码注入 IBroadPhase2D");
            if (!string.IsNullOrEmpty(world.LastError))
            {
                EditorGUILayout.HelpBox(world.LastError, MessageType.Error);
            }

            if (GUILayout.Button("检查平面一致性（包含禁用 Body）"))
            {
                _checkPassed = CollisionWorldValidation.CheckPlaneConsistency(world, _mismatches, out _, out _checkMessage);
            }

            if (_checkMessage != null)
            {
                EditorGUILayout.HelpBox(_checkMessage, _checkPassed ? MessageType.Info : MessageType.Warning);
                using (new EditorGUI.DisabledScope(true))
                {
                    foreach (CollisionBodyBehaviour body in _mismatches)
                    {
                        EditorGUILayout.ObjectField("平面不一致", body, typeof(CollisionBodyBehaviour), true);
                    }
                }
            }
        }
    }
}
