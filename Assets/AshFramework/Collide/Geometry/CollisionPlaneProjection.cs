using System;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>固定世界原点下的轴映射，不量化、不缩放、不读取逻辑高度。</summary>
    public static class CollisionPlaneProjection
    {
        /// <summary>位置或方向的分量映射；方向不会自动归一化。</summary>
        public static FixVector2 Project(FixVector3 worldPosition, CollisionPlane plane)
        {
            switch (plane)
            {
                case CollisionPlane.XY:
                    return new FixVector2(worldPosition.X, worldPosition.Y);
                case CollisionPlane.XZ:
                    return new FixVector2(worldPosition.X, worldPosition.Z);
                default:
                    throw new ArgumentOutOfRangeException(nameof(plane));
            }
        }

        /// <summary>
        /// 还原到三维坐标。normalCoordinate 在 XY 模式为世界 Z，在 XZ 模式为世界 Y，
        /// 与角色的逻辑高度 1/2/3 无关。
        /// </summary>
        public static FixVector3 Embed(FixVector2 position, Fix64 normalCoordinate, CollisionPlane plane)
        {
            switch (plane)
            {
                case CollisionPlane.XY:
                    return new FixVector3(position.X, position.Y, normalCoordinate);
                case CollisionPlane.XZ:
                    return new FixVector3(position.X, normalCoordinate, position.Y);
                default:
                    throw new ArgumentOutOfRangeException(nameof(plane));
            }
        }

        /// <summary>
        /// 从世界局部 X 轴的平面投影得到逆时针弧度。零投影返回 false、rotation=0。
        /// 本方法只处理方向，物体是否发生平面外倾斜由调用者验证。
        /// </summary>
        public static bool TryGetRotation(FixVector3 worldAxisX, CollisionPlane plane, out Fix64 rotation)
        {
            FixVector2 direction = Project(worldAxisX, plane);
            if (direction == FixVector2.Zero)
            {
                rotation = Fix64.Zero;
                return false;
            }

            rotation = FixMath.Atan2(direction.Y, direction.X);
            return true;
        }
    }
}
