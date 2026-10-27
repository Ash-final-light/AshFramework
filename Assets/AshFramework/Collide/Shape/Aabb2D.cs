using System;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>世界轴对齐矩形。HalfExtents 为正半尺寸，边界采用闭区间。</summary>
    public readonly struct Aabb2D
    {
        public FixVector2 Center { get; }
        public FixVector2 HalfExtents { get; }
        public bool IsValid => HalfExtents.X > Fix64.Zero && HalfExtents.Y > Fix64.Zero;

        public FixVector2 Min
        {
            get
            {
                Validate();
                return Center - HalfExtents;
            }
        }

        public FixVector2 Max
        {
            get
            {
                Validate();
                return Center + HalfExtents;
            }
        }

        public Aabb2D(FixVector2 center, FixVector2 halfExtents)
        {
            GeometryPredicates.ValidateHalfExtents(halfExtents);
            GeometryPredicates.ValidateBounds(center, halfExtents);
            Center = center;
            HalfExtents = halfExtents;
        }

        public Aabb2D GetBounds()
        {
            Validate();
            return this;
        }

        internal void Validate()
        {
            if (!IsValid)
            {
                throw new ArgumentException("An uninitialized AABB is not a valid shape.");
            }
        }
    }
}
