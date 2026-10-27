using System;
using AshFramework.Math;
using AshFramework.Math.Unity;
using UnityEngine;

namespace AshFramework.Collide.Unity
{
    /// <summary>Unity 浮点姿态的显式输入边界，不反向写入 Transform。</summary>
    public static class UnityCollisionPlaneAdapter
    {
        /// <summary>
        /// 旋转后平面法线与基准法线之间的最大弦长，约对应 0.000573 度。
        /// 仅处理姿态读取误差，不是精筛接触容差。
        /// </summary>
        public const float DefaultPlaneTolerance = 0.00001f;

        /// <summary>
        /// 读取世界位置与平面内旋转，输出 Scale=One；不读取或烘焙 Transform 缩放。
        /// 空/已销毁对象、平面外旋转或不可表示的位置返回 false，输出 Identity。
        /// 非法 plane 或非有限/不在 [0,1) 内的容差抛出参数异常。
        /// </summary>
        public static bool TryReadRigidPose(Transform transform, CollisionPlane plane,
            out FixTransform2D pose, float planeTolerance = DefaultPlaneTolerance)
        {
            Vector3 normal = GetNormal(plane);
            if (!IsFinite(planeTolerance) || planeTolerance < 0 || planeTolerance >= 1)
            {
                throw new ArgumentOutOfRangeException(nameof(planeTolerance));
            }

            pose = FixTransform2D.Identity;
            if (transform == null)
            {
                return false;
            }

            Quaternion rotation = transform.rotation;
            if (!IsFinite(rotation.x) || !IsFinite(rotation.y)
                || !IsFinite(rotation.z) || !IsFinite(rotation.w))
            {
                return false;
            }

            double normSquared = (double)rotation.x * rotation.x + (double)rotation.y * rotation.y
                + (double)rotation.z * rotation.z + (double)rotation.w * rotation.w;
            if (System.Math.Abs(normSquared - 1) > 0.0001)
            {
                return false;
            }

            Vector3 rotatedNormal = rotation * normal;
            double dx = (double)rotatedNormal.x - normal.x;
            double dy = (double)rotatedNormal.y - normal.y;
            double dz = (double)rotatedNormal.z - normal.z;
            if (dx * dx + dy * dy + dz * dz > (double)planeTolerance * planeTolerance)
            {
                return false;
            }

            Vector3 position = transform.position;
            Vector2 planarPosition = plane == CollisionPlane.XY
                ? new Vector2(position.x, position.y)
                : new Vector2(position.x, position.z);
            if (!IsFinite(planarPosition.x) || !IsFinite(planarPosition.y))
            {
                return false;
            }

            Vector3 axisX = rotation * Vector3.right;
            if (!IsFinite(axisX.x) || !IsFinite(axisX.y) || !IsFinite(axisX.z))
            {
                return false;
            }

            try
            {
                FixVector2 center = planarPosition.ToFixVector();
                if (!CollisionPlaneProjection.TryGetRotation(axisX.ToFixVector(), plane, out Fix64 angle))
                {
                    return false;
                }

                pose = new FixTransform2D(center, angle, FixVector2.One);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static Vector3 GetNormal(CollisionPlane plane)
        {
            switch (plane)
            {
                case CollisionPlane.XY:
                    return Vector3.forward;
                case CollisionPlane.XZ:
                    return Vector3.up;
                default:
                    throw new ArgumentOutOfRangeException(nameof(plane));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
