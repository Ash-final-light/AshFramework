using System;

namespace AshFramework.Collide
{
    /// <summary>共用简单形状精筛，不读取 Unity 对象、逻辑高度或事件状态。</summary>
    public static class CompoundCollision2D
    {
        public static bool Overlaps(CompoundShape2D a, CompoundShape2D b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            if (!a.TryGetBounds(out Aabb2D boundsA) || !b.TryGetBounds(out Aabb2D boundsB)
                || !Collision2D.Overlaps(boundsA, boundsB))
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                WorldShape2D shapeA = a[i];
                for (int j = 0; j < b.Count; j++)
                {
                    WorldShape2D shapeB = b[j];
                    if (Collision2D.Overlaps(shapeA.GetBounds(), shapeB.GetBounds()) && Overlaps(shapeA, shapeB))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool Overlaps(WorldShape2D a, WorldShape2D b)
        {
            if (!a.IsValid || !b.IsValid)
            {
                throw new ArgumentException("Both world shapes must be valid.");
            }

            if ((int)a.Kind > (int)b.Kind)
            {
                return Overlaps(b, a);
            }

            switch (a.Kind)
            {
                case ShapeKind2D.Circle:
                    switch (b.Kind)
                    {
                        case ShapeKind2D.Circle:
                            return Collision2D.Overlaps(a.Circle, b.Circle);
                        case ShapeKind2D.Aabb:
                            return Collision2D.Overlaps(a.Circle, b.Aabb);
                        case ShapeKind2D.Obb:
                            return Collision2D.Overlaps(a.Circle, b.Obb);
                    }

                    break;
                case ShapeKind2D.Aabb:
                    return b.Kind == ShapeKind2D.Aabb
                        ? Collision2D.Overlaps(a.Aabb, b.Aabb)
                        : Collision2D.Overlaps(a.Aabb, b.Obb);
                case ShapeKind2D.Obb:
                    return Collision2D.Overlaps(a.Obb, b.Obb);
            }

            throw new ArgumentException("Unsupported world shape kind.");
        }
    }
}
