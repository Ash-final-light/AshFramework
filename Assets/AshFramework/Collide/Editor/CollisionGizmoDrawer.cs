using System.Collections.Generic;
using AshFramework.Collide.Unity;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Collide.Editor
{
    public static class CollisionGizmoDrawer
    {
        private static readonly CollisionGizmoPreview Preview = new CollisionGizmoPreview();
        private static readonly List<Vector3> Points = new List<Vector3>(65);
        private static readonly Color ShapeColor = new Color(0.3f, 1f, 0.35f, 1f);
        private static readonly Color ShapeBoundsColor = new Color(1f, 0.75f, 0.15f, 0.65f);
        private static readonly Color BodyBoundsColor = new Color(0.15f, 0.85f, 1f, 1f);
        private static GUIStyle _labelStyle;

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawBody(CollisionBodyBehaviour body, GizmoType gizmoType)
        {
            if (body == null || !body.isActiveAndEnabled)
            {
                return;
            }

            bool selected = IsBodySelected(body);
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousHandlesColor = Handles.color;
            try
            {
                Gizmos.matrix = Matrix4x4.identity;
                if (!Preview.Capture(body))
                {
                    if (selected)
                    {
                        DrawError(body.transform.position, Preview.Error);
                    }

                    return;
                }

                for (int i = 0; i < Preview.Geometry.Count; i++)
                {
                    WorldShape2D shape = Preview.Geometry[i];
                    if (selected)
                    {
                        DrawOutline(WorldShape2D.FromAabb(shape.GetBounds()), ShapeBoundsColor);
                    }

                    DrawOutline(shape, ShapeColor);
                }

                if (selected && Preview.Geometry.TryGetBounds(out Aabb2D bounds))
                {
                    DrawOutline(WorldShape2D.FromAabb(bounds), BodyBoundsColor);
                    DrawLabel(body.transform.position,
                        "Body AABB | " + body.Plane + " | Height " + (int)body.LogicalHeight
                        + " | Shapes " + Preview.Geometry.Count, BodyBoundsColor);
                }
            }
            finally
            {
                Gizmos.color = previousColor;
                Gizmos.matrix = previousMatrix;
                Handles.color = previousHandlesColor;
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawOrphanShape(CollisionShapeBehaviour shape, GizmoType gizmoType)
        {
            if (shape == null || !shape.isActiveAndEnabled || shape.Owner != null
                || !Selection.Contains(shape.gameObject))
            {
                return;
            }

            Color previousColor = Handles.color;
            try
            {
                DrawError(shape.transform.position, "Shape requires an ancestor Collision Body.");
            }
            finally
            {
                Handles.color = previousColor;
            }
        }

        private static bool IsBodySelected(CollisionBodyBehaviour body)
        {
            foreach (Transform selected in Selection.transforms)
            {
                if (selected.GetComponentInParent<CollisionBodyBehaviour>(true) == body)
                {
                    return true;
                }
            }

            return false;
        }

        private static void DrawOutline(WorldShape2D shape, Color color)
        {
            CollisionGizmoPreview.BuildOutline(shape, Preview.Plane, Preview.NormalCoordinate, Points);
            Gizmos.color = color;
            for (int i = 1; i < Points.Count; i++)
            {
                Gizmos.DrawLine(Points[i - 1], Points[i]);
            }
        }

        private static void DrawError(Vector3 position, string error)
        {
            if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)
                || float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
            {
                return;
            }

            Handles.color = Color.red;
            float size = HandleUtility.GetHandleSize(position) * 0.08f;
            Handles.DrawLine(position - Vector3.right * size, position + Vector3.right * size);
            Handles.DrawLine(position - Vector3.up * size, position + Vector3.up * size);
            Handles.DrawLine(position - Vector3.forward * size, position + Vector3.forward * size);
            DrawLabel(position, "Collision: " + error, Color.red);
        }

        private static void DrawLabel(Vector3 position, string text, Color color)
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.label);
            }

            _labelStyle.normal.textColor = color;
            Handles.Label(position, text, _labelStyle);
        }
    }
}
