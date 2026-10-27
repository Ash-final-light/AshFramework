using System;
using System.Collections.Generic;
using AshFramework.Collide.Unity;
using AshFramework.Math.Unity;
using UnityEngine;

namespace AshFramework.Collide.Editor
{
    /// <summary>仅供 Editor 使用的独立预览，不刷新或改写 Body 的检测缓存。</summary>
    public sealed class CollisionGizmoPreview
    {
        private readonly List<WorldShape2D> _shapes = new List<WorldShape2D>();

        public CompoundShape2D Geometry { get; } = new CompoundShape2D();
        public CollisionPlane Plane { get; private set; }
        public float NormalCoordinate { get; private set; }
        public string Error { get; private set; } = string.Empty;

        public bool Capture(CollisionBodyBehaviour body)
        {
            Geometry.Clear();
            _shapes.Clear();
            Error = string.Empty;
            if (body == null || !body.isActiveAndEnabled)
            {
                return Fail("Body is disabled or inactive.");
            }

            Plane = body.Plane;
            if (Plane != CollisionPlane.XY && Plane != CollisionPlane.XZ)
            {
                return Fail("Body plane is invalid.");
            }

            if (body.LogicalHeight != LogicalHeight.Level1 && body.LogicalHeight != LogicalHeight.Level2
                && body.LogicalHeight != LogicalHeight.Level3)
            {
                return Fail("Body logical height must be 1, 2 or 3.");
            }

            Vector3 position = body.transform.position;
            NormalCoordinate = Plane == CollisionPlane.XY ? position.z : position.y;
            if (float.IsNaN(NormalCoordinate) || float.IsInfinity(NormalCoordinate))
            {
                return Fail("Body display plane has a non-finite coordinate.");
            }

            CollisionShapeBehaviour[] candidates = body.GetComponentsInChildren<CollisionShapeBehaviour>(true);
            foreach (CollisionShapeBehaviour shape in candidates)
            {
                if (!shape.isActiveAndEnabled || shape.Owner != body)
                {
                    continue;
                }

                if (!shape.TryBuildWorldShape(Plane, out WorldShape2D worldShape, out string error))
                {
                    return Fail(shape.name + ": " + error);
                }

                _shapes.Add(worldShape);
            }

            if (_shapes.Count == 0)
            {
                return Fail("Body has no active owned shapes.");
            }

            try
            {
                Geometry.SetShapes(_shapes);
            }
            catch (ArgumentException exception)
            {
                return Fail(exception.Message);
            }
            catch (OverflowException exception)
            {
                return Fail(exception.Message);
            }

            return true;
        }

        private bool Fail(string error)
        {
            Geometry.Clear();
            _shapes.Clear();
            Error = error;
            return false;
        }

        /// <summary>
        /// 输出闭合世界折线。圆以折线近似；矩形使用实际量化轴，不再单位化或读取 Transform。
        /// double 仅用于 Editor 表现，避免对 OBB 的定点轴乘积再次作定点截断。
        /// </summary>
        public static void BuildOutline(WorldShape2D shape, CollisionPlane plane, float normalCoordinate,
            List<Vector3> points, int circleSegments = 64)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            points.Clear();
            if (!shape.IsValid || (plane != CollisionPlane.XY && plane != CollisionPlane.XZ)
                || float.IsNaN(normalCoordinate) || float.IsInfinity(normalCoordinate) || circleSegments < 8)
            {
                throw new ArgumentException("A valid shape, plane, display coordinate and at least 8 segments are required.");
            }

            if (shape.Kind == ShapeKind2D.Circle)
            {
                Circle2D circle = shape.Circle;
                for (int i = 0; i < circleSegments; i++)
                {
                    double angle = i * (2.0 * System.Math.PI / circleSegments);
                    AddPoint(points, plane, normalCoordinate,
                        circle.Center.X.ToDouble() + circle.Radius.ToDouble() * System.Math.Cos(angle),
                        circle.Center.Y.ToDouble() + circle.Radius.ToDouble() * System.Math.Sin(angle));
                }
            }
            else if (shape.Kind == ShapeKind2D.Aabb)
            {
                Aabb2D box = shape.Aabb;
                AddPoint(points, plane, normalCoordinate, box.Min.X.ToDouble(), box.Min.Y.ToDouble());
                AddPoint(points, plane, normalCoordinate, box.Max.X.ToDouble(), box.Min.Y.ToDouble());
                AddPoint(points, plane, normalCoordinate, box.Max.X.ToDouble(), box.Max.Y.ToDouble());
                AddPoint(points, plane, normalCoordinate, box.Min.X.ToDouble(), box.Max.Y.ToDouble());
            }
            else
            {
                Obb2D box = shape.Obb;
                double x = box.AxisX.X.ToDouble() * box.HalfExtents.X.ToDouble();
                double y = box.AxisX.Y.ToDouble() * box.HalfExtents.X.ToDouble();
                double u = box.AxisY.X.ToDouble() * box.HalfExtents.Y.ToDouble();
                double v = box.AxisY.Y.ToDouble() * box.HalfExtents.Y.ToDouble();
                double centerX = box.Center.X.ToDouble();
                double centerY = box.Center.Y.ToDouble();
                AddPoint(points, plane, normalCoordinate, centerX - x - u, centerY - y - v);
                AddPoint(points, plane, normalCoordinate, centerX + x - u, centerY + y - v);
                AddPoint(points, plane, normalCoordinate, centerX + x + u, centerY + y + v);
                AddPoint(points, plane, normalCoordinate, centerX - x + u, centerY - y + v);
            }

            points.Add(points[0]);
        }

        private static void AddPoint(List<Vector3> points, CollisionPlane plane, float normal, double x, double y)
        {
            points.Add(plane == CollisionPlane.XY
                ? new Vector3((float)x, (float)y, normal)
                : new Vector3((float)x, normal, (float)y));
        }
    }
}
