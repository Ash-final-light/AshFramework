using System;
using AshFramework.Math;
using AshFramework.Math.Unity;
using UnityEngine;

namespace AshFramework.Collide.Unity
{
    internal static class ShapeGeometryPreparation
    {
        internal const double ScaleTolerance = 0.00001;

        internal static bool TryReadFrame(Transform source, Vector2 localCenter, CollisionPlane plane,
            out FixTransform2D pose, out FixVector2 scale, out string error)
        {
            pose = FixTransform2D.Identity;
            scale = FixVector2.Zero;
            error = string.Empty;
            if (!IsFinite(localCenter.x) || !IsFinite(localCenter.y))
            {
                error = "Local center must be finite.";
                return false;
            }

            if (!UnityCollisionPlaneAdapter.TryReadRigidPose(source, plane, out _))
            {
                error = "Transform has an invalid position or an out-of-plane rotation.";
                return false;
            }

            for (Transform current = source; current != null; current = current.parent)
            {
                Vector3 localScale = current.localScale;
                if (!IsFinite(localScale.x) || !IsFinite(localScale.y) || !IsFinite(localScale.z)
                    || localScale.x <= 0 || localScale.y <= 0 || localScale.z <= 0)
                {
                    error = "Shape hierarchy requires positive finite scale on every axis.";
                    return false;
                }
            }

            Matrix4x4 matrix = source.localToWorldMatrix;
            Vector3 worldX = matrix.MultiplyVector(Vector3.right);
            Vector3 worldY = matrix.MultiplyVector(plane == CollisionPlane.XY ? Vector3.up : Vector3.forward);
            Vector2 x = Project(worldX, plane);
            Vector2 y = Project(worldY, plane);
            if (!IsFinite(worldX.x) || !IsFinite(worldX.y) || !IsFinite(worldX.z)
                || !IsFinite(worldY.x) || !IsFinite(worldY.y) || !IsFinite(worldY.z))
            {
                error = "World shape axes must be finite.";
                return false;
            }

            double lengthX = System.Math.Sqrt((double)x.x * x.x + (double)x.y * x.y);
            double lengthY = System.Math.Sqrt((double)y.x * y.x + (double)y.y * y.y);
            double normalX = plane == CollisionPlane.XY ? worldX.z : worldX.y;
            double normalY = plane == CollisionPlane.XY ? worldY.z : worldY.y;
            double dot = (double)x.x * y.x + (double)x.y * y.y;
            double determinant = (double)x.x * y.y - (double)x.y * y.x;
            if (lengthX == 0 || lengthY == 0 || determinant <= 0
                || System.Math.Abs(dot) > ScaleTolerance * lengthX * lengthY
                || System.Math.Abs(normalX) > ScaleTolerance * lengthX
                || System.Math.Abs(normalY) > ScaleTolerance * lengthY)
            {
                error = "World shape axes must stay in-plane and orthogonal; shear is unsupported.";
                return false;
            }

            Vector3 local = plane == CollisionPlane.XY
                ? new Vector3(localCenter.x, localCenter.y, 0)
                : new Vector3(localCenter.x, 0, localCenter.y);
            Vector2 center = Project(matrix.MultiplyPoint3x4(local), plane);
            if (!IsFinite(center.x) || !IsFinite(center.y))
            {
                error = "World shape center must be finite.";
                return false;
            }

            try
            {
                FixVector2 direction = new FixVector2((x.x / lengthX).ToFix64(), (x.y / lengthX).ToFix64());
                scale = new FixVector2(lengthX.ToFix64(), lengthY.ToFix64());
                if (scale.X <= Fix64.Zero || scale.Y <= Fix64.Zero)
                {
                    error = "World scale is below fixed-point resolution.";
                    return false;
                }

                pose = new FixTransform2D(center.ToFixVector(),
                    FixMath.Atan2(direction.Y, direction.X), FixVector2.One);
                return true;
            }
            catch (OverflowException)
            {
                error = "World geometry exceeds fixed-point range.";
                return false;
            }
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Vector2 Project(Vector3 value, CollisionPlane plane)
        {
            return plane == CollisionPlane.XY ? new Vector2(value.x, value.y) : new Vector2(value.x, value.z);
        }
    }
}
