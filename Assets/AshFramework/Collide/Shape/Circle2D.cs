using System;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>世界空间圆。半径必须为正，整个包围盒必须可用 Fix64 表示。</summary>
    public readonly struct Circle2D
    {
        public FixVector2 Center { get; }
        public Fix64 Radius { get; }
        public bool IsValid => Radius > Fix64.Zero;

        public Circle2D(FixVector2 center, Fix64 radius)
        {
            if (radius <= Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive.");
            }

            GeometryPredicates.ValidateBounds(center, new FixVector2(radius, radius));
            Center = center;
            Radius = radius;
        }

        public Aabb2D GetBounds()
        {
            Validate();
            return new Aabb2D(Center, new FixVector2(Radius, Radius));
        }

        internal void Validate()
        {
            if (!IsValid)
            {
                throw new ArgumentException("An uninitialized circle is not a valid shape.");
            }
        }
    }
}
