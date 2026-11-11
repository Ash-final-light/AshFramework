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
            EditorGUILayout.HelpBox("均匀网格通过接口显式注入，World/Body 不依赖其具体类型。进入 Play 会初始化，点击 Step 才检测；编辑状态也可手动初始化和 Step。红线及列表对应上次成功 Step。", MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("示例 World", overview.World, typeof(CollisionWorldBehaviour), true);
            }

            serializedObject.Update();
            using (new EditorGUI.DisabledScope(overview.World != null && overview.World.IsInitialized))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_cellSize"), new GUIContent("示例格子边长"));
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(overview.IndexMessage, MessageType.Info);
            using (new EditorGUI.DisabledScope(overview.World == null || overview.World.IsInitialized))
            {
                if (GUILayout.Button("显式初始化均匀网格"))
                {
                    overview.InitializeGrid();
                    SceneView.RepaintAll();
                }
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
            if (GUILayout.Button("执行显式 Step"))
            {
                overview.AttemptStep();
                SceneView.RepaintAll();
            }

            if (overview.HasCorrectionExample)
            {
                using (new EditorGUI.DisabledScope(overview.World == null || !overview.World.IsInitialized))
                {
                    if (GUILayout.Button("移向墙面并修正"))
                    {
                        Undo.RecordObject(overview.CorrectionTransform, "Move character against wall");
                        overview.AttemptCorrection(false);
                        SceneView.RepaintAll();
                    }

                    if (GUILayout.Button("移入触发区"))
                    {
                        Undo.RecordObject(overview.CorrectionTransform, "Move character into trigger");
                        overview.AttemptCorrection(true);
                        SceneView.RepaintAll();
                    }
                }
            }

            EditorGUILayout.HelpBox(overview.StepMessage, MessageType.Info);
            EditorGUILayout.LabelField("上次成功 Step 的配对和接触，修改后需再次 Step。", EditorStyles.wordWrappedLabel);
            for (int i = 0; i < overview.PairCount; i++)
            {
                EditorGUILayout.LabelField(overview.GetPairDescription(i), EditorStyles.wordWrappedLabel);
            }

            for (int i = 0; i < overview.ContactCount; i++)
            {
                EditorGUILayout.LabelField(overview.GetContactDescription(i), EditorStyles.wordWrappedLabel);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Play：移开 / 还原 B，随后点击 Step"))
                {
                    overview.ToggleExampleBPosition();
                }

                if (GUILayout.Button("Play：禁用 / 启用 B，随后点击 Step"))
                {
                    overview.ToggleExampleBEnabled();
                }
            }

            for (int i = 0; i < overview.CaseCount; i++)
            {
                CollisionWorldOverview.Entry entry = overview.GetCase(i);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(entry.Description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(overview.GetBodyStatus(i), EditorStyles.wordWrappedLabel);
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
                DrawGrid(overview, worldCenter, half, xy);
                Label(new Vector3(-9, 5.5f, 0), "均匀网格碰撞世界 | XY 示例", Color.cyan);
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

                if (overview.LastStepSucceeded)
                {
                    Gizmos.color = Color.red;
                    for (int i = 0; i < overview.PairCount; i++)
                    {
                        CollisionPair pair = overview.GetPair(i);
                        CollisionBodyBehaviour a = overview.FindBody(pair.A);
                        CollisionBodyBehaviour b = overview.FindBody(pair.B);
                        if (a != null && b != null)
                        {
                            Gizmos.DrawLine(a.transform.position, b.transform.position);
                        }
                    }

                    DrawContactArrows(overview, xy ? Vector3.forward : Vector3.up);
                }
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
                Handles.color = previousHandlesColor;
            }
        }

        private static void DrawContactArrows(CollisionWorldOverview overview, Vector3 planeNormal)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < overview.ContactCount; i++)
            {
                if (!overview.TryGetContactGizmo(i, out Vector3 origin, out Vector3 direction, out string depth))
                {
                    continue;
                }

                Vector3 tip = origin + direction * 0.65f;
                Vector3 head = tip - direction * 0.18f;
                Vector3 side = Vector3.Cross(planeNormal, direction) * 0.10f;
                Gizmos.DrawLine(origin, tip);
                Gizmos.DrawLine(tip, head + side);
                Gizmos.DrawLine(tip, head - side);
                Label(tip + side, depth, Color.yellow);
            }
        }

        private static void DrawGrid(CollisionWorldOverview overview, Vector3 center, Vector2 half, bool xy)
        {
            if (overview.GridColumns + overview.GridRows > 1024)
            {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.6f, 1, 0.25f);
            for (int x = 1; x < overview.GridColumns; x++)
            {
                float offset = -half.x + x * overview.GridCellSize;
                Gizmos.DrawLine(center + new Vector3(offset, xy ? -half.y : 0, xy ? 0 : -half.y),
                    center + new Vector3(offset, xy ? half.y : 0, xy ? 0 : half.y));
            }

            for (int y = 1; y < overview.GridRows; y++)
            {
                float offset = -half.y + y * overview.GridCellSize;
                Gizmos.DrawLine(center + new Vector3(-half.x, xy ? offset : 0, xy ? 0 : offset),
                    center + new Vector3(half.x, xy ? offset : 0, xy ? 0 : offset));
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
