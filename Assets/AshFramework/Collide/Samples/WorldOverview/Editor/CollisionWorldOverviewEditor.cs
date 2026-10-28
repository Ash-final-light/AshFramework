using AshFramework.Collide.Unity;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Collide.Samples.Editor
{
    [CustomEditor(typeof(CollisionWorldOverview))]
    public sealed class CollisionWorldOverviewEditor : UnityEditor.Editor
    {
        private static GUIStyle _labelStyle;

        public override void OnInspectorGUI()
        {
            CollisionWorldOverview overview = (CollisionWorldOverview)target;
            EditorGUILayout.HelpBox("当前阶段：世界接口与 Unity 绑定已实现，尚无具体空间索引。Scene 轮廓是几何配置预览，不是世界检测结果。无需进入 Play 即可查看此说明与检查按钮。", MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("示例 World", overview.World, typeof(CollisionWorldBehaviour), true);
            }

            if (overview.World != null && GUILayout.Button("选择 World，使用平面一致性检查按钮"))
            {
                Selection.activeGameObject = overview.World.gameObject;
            }

            if (GUILayout.Button("刷新示例几何（不提交世界）"))
            {
                overview.RefreshExampleGeometry();
                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox(overview.GeometryMessage, MessageType.None);
            if (GUILayout.Button("尝试显式 Step（初始配置预期失败）"))
            {
                overview.AttemptStep();
            }

            EditorGUILayout.HelpBox(overview.StepMessage, MessageType.Info);
            for (int i = 0; i < overview.CaseCount; i++)
            {
                CollisionWorldOverview.Entry entry = overview.GetCase(i);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(entry.Description, EditorStyles.wordWrappedLabel);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(entry.Body, typeof(CollisionBodyBehaviour), true);
                }

                if (entry.Body != null && GUILayout.Button("选择 " + entry.Body.name))
                {
                    Selection.activeGameObject = entry.Body.gameObject;
                }
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawOverview(CollisionWorldOverview overview, GizmoType gizmoType)
        {
            if (overview.World == null)
            {
                return;
            }

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousHandlesColor = Handles.color;
            try
            {
                Gizmos.matrix = Matrix4x4.identity;
                Vector2 center = overview.World.BoundsCenter;
                Vector2 half = overview.World.BoundsHalfExtents;
                bool xy = overview.World.Plane == CollisionPlane.XY;
                Vector3 worldCenter = xy ? new Vector3(center.x, center.y, 0) : new Vector3(center.x, 0, center.y);
                Vector3 size = xy ? new Vector3(half.x * 2, half.y * 2, 0) : new Vector3(half.x * 2, 0, half.y * 2);
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(worldCenter, size);
                Label(new Vector3(-9, 5.5f, 0), "接口版碰撞世界 | XY 配置说明", Color.cyan);
                Label(new Vector3(-9, -4.6f, 0), "青框：固定范围 | 绿色：几何预览 | 灰叉：禁用标记", Color.cyan);
                for (int i = 0; i < overview.CaseCount; i++)
                {
                    CollisionWorldOverview.Entry entry = overview.GetCase(i);
                    CollisionBodyBehaviour body = entry.Body;
                    if (body == null)
                    {
                        continue;
                    }

                    Color color = body.World == null ? Color.magenta : Color.white;
                    if (body.Plane != overview.World.Plane)
                    {
                        color = new Color(1, 0.65f, 0.2f);
                    }

                    if (!body.isActiveAndEnabled)
                    {
                        Gizmos.color = Color.gray;
                        Vector3 marker = body.transform.position;
                        Gizmos.DrawLine(marker + new Vector3(-0.35f, -0.35f, 0), marker + new Vector3(0.35f, 0.35f, 0));
                        Gizmos.DrawLine(marker + new Vector3(-0.35f, 0.35f, 0), marker + new Vector3(0.35f, -0.35f, 0));
                    }

                    string state = body.isActiveAndEnabled ? "启用" : "禁用";
                    Label(body.transform.position + entry.LabelOffset, body.name + "\n" + body.Plane + " | " + state, color);
                }
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
                Handles.color = previousHandlesColor;
            }
        }

        private static void Label(Vector3 position, string text, Color color)
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14
                };
            }

            _labelStyle.normal.textColor = color;
            Handles.Label(position, text, _labelStyle);
        }
    }
}
