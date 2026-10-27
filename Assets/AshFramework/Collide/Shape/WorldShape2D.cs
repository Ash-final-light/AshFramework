using System;

namespace AshFramework.Collide
{
    /// <summary>通过类型化工厂创建的世界形状。default 为 None，无效。</summary>
    public readonly struct WorldShape2D
    {
        private readonly Circle2D _circle;
        private readonly Aabb2D _aabb;
        private readonly Obb2D _obb;
        private readonly Aabb2D _bounds;

        public ShapeKind2D Kind { get; }
        public bool IsValid => Kind != ShapeKind2D.None;

        public Circle2D Circle
        {
            get
            {
                RequireKind(ShapeKind2D.Circle);
                return _circle;
            }
        }

        public Aabb2D Aabb
        {
            get
            {
                RequireKind(ShapeKind2D.Aabb);
                return _aabb;
            }
        }

        public Obb2D Obb
        {
            get
            {
                RequireKind(ShapeKind2D.Obb);
                return _obb;
            }
        }

        private WorldShape2D(Circle2D circle, Aabb2D aabb, Obb2D obb, ShapeKind2D kind, Aabb2D bounds)
        {
            _circle = circle;
            _aabb = aabb;
            _obb = obb;
            Kind = kind;
            _bounds = bounds;
        }

        public static WorldShape2D FromCircle(Circle2D circle)
        {
            return new WorldShape2D(circle, default, default, ShapeKind2D.Circle, circle.GetBounds());
        }

        public static WorldShape2D FromAabb(Aabb2D aabb)
        {
            return new WorldShape2D(default, aabb, default, ShapeKind2D.Aabb, aabb.GetBounds());
        }

        public static WorldShape2D FromObb(Obb2D obb)
        {
            return new WorldShape2D(default, default, obb, ShapeKind2D.Obb, obb.GetBounds());
        }

        public Aabb2D GetBounds()
        {
            if (!IsValid)
            {
                throw new ArgumentException("An uninitialized world shape is invalid.");
            }

            return _bounds;
        }

        private void RequireKind(ShapeKind2D expected)
        {
            if (Kind != expected)
            {
                throw new InvalidOperationException("World shape has a different geometry kind.");
            }
        }
    }
}
