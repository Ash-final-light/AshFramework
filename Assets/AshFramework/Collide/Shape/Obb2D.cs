using System;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>
    /// 世界旋转矩形，角度为逆时针弧度。几何由量化后的 AxisX/AxisY 与局部半尺寸定义。
    /// 轴严格互相垂直、长度相等，但量化后长度不保证数学上恰好为一。
    /// </summary>
    public readonly struct Obb2D
    {
        private readonly Aabb2D _bounds;

        public FixVector2 Center { get; }
        public FixVector2 HalfExtents { get; }
        public Fix64 Rotation { get; }
        public FixVector2 AxisX { get; }
        public FixVector2 AxisY { get; }
        public bool IsValid => HalfExtents.X > Fix64.Zero && HalfExtents.Y > Fix64.Zero;

        public Obb2D(FixVector2 center, FixVector2 halfExtents, Fix64 rotation)
        {
            GeometryPredicates.ValidateHalfExtents(halfExtents);
            Fix64 sin;
            Fix64 cos;
            if (rotation == Fix64.Zero)
            {
                sin = Fix64.Zero;
                cos = Fix64.One;
            }
            else if (rotation == FixMath.HalfPi || rotation == -FixMath.HalfPi)
            {
                sin = rotation > Fix64.Zero ? Fix64.One : -Fix64.One;
                cos = Fix64.Zero;
            }
            else if (rotation == FixMath.Pi || rotation == -FixMath.Pi)
            {
                sin = Fix64.Zero;
                cos = -Fix64.One;
            }
            else
            {
                FixMath.SinCos(rotation, out sin, out cos);
            }

            Center = center;
            HalfExtents = halfExtents;
            Rotation = rotation;
            AxisX = new FixVector2(cos, sin);
            AxisY = new FixVector2(-sin, cos);
            _bounds = GeometryPredicates.GetObbBounds(center, halfExtents, AxisX);
        }

        /// <summary>按轴投影向外取整，保守包含实际使用的量化轴矩形。</summary>
        public Aabb2D GetBounds()
        {
            Validate();
            return _bounds;
        }

        internal void Validate()
        {
            if (!IsValid)
            {
                throw new ArgumentException("An uninitialized OBB is not a valid shape.");
            }
        }
    }
}
