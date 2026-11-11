using System;
using System.Collections.Generic;

namespace AshFramework.Collide
{
    /// <summary>共用简单形状精筛，不读取 Unity 对象、逻辑高度或事件状态。</summary>
    public static class CompoundCollision2D
    {
        public static bool Overlaps(CompoundShape2D a, CompoundShape2D b)
        {
            if (!BoundsOverlap(a, b))
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

        /// <summary>
        /// 清空 results，按 A 索引、B 索引的顺序收集全部命中子形状对。失败时 results 为空。
        /// </summary>
        public static void CollectContacts(CompoundShape2D a, CompoundShape2D b, List<CompoundContact2D> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            if (!BoundsOverlap(a, b))
            {
                return;
            }

            try
            {
                for (int i = 0; i < a.Count; i++)
                {
                    WorldShape2D shapeA = a[i];
                    Aabb2D boundsA = shapeA.GetBounds();
                    for (int j = 0; j < b.Count; j++)
                    {
                        WorldShape2D shapeB = b[j];
                        if (Collision2D.Overlaps(boundsA, shapeB.GetBounds())
                            && TryGetContact(shapeA, shapeB, out Contact2D contact))
                        {
                            results.Add(new CompoundContact2D(i, j, contact));
                        }
                    }
                }
            }
            catch
            {
                results.Clear();
                throw;
            }
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

        private static bool BoundsOverlap(CompoundShape2D a, CompoundShape2D b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            return a.TryGetBounds(out Aabb2D boundsA) && b.TryGetBounds(out Aabb2D boundsB)
                && Collision2D.Overlaps(boundsA, boundsB);
        }

        private static bool TryGetContact(WorldShape2D a, WorldShape2D b, out Contact2D contact)
        {
            switch (a.Kind, b.Kind)
            {
                case (ShapeKind2D.Circle, ShapeKind2D.Circle):
                    return Collision2D.TryGetContact(a.Circle, b.Circle, out contact);
                case (ShapeKind2D.Circle, ShapeKind2D.Aabb):
                    return Collision2D.TryGetContact(a.Circle, b.Aabb, out contact);
                case (ShapeKind2D.Circle, ShapeKind2D.Obb):
                    return Collision2D.TryGetContact(a.Circle, b.Obb, out contact);
                case (ShapeKind2D.Aabb, ShapeKind2D.Circle):
                    return Collision2D.TryGetContact(a.Aabb, b.Circle, out contact);
                case (ShapeKind2D.Aabb, ShapeKind2D.Aabb):
                    return Collision2D.TryGetContact(a.Aabb, b.Aabb, out contact);
                case (ShapeKind2D.Aabb, ShapeKind2D.Obb):
                    return Collision2D.TryGetContact(a.Aabb, b.Obb, out contact);
                case (ShapeKind2D.Obb, ShapeKind2D.Circle):
                    return Collision2D.TryGetContact(a.Obb, b.Circle, out contact);
                case (ShapeKind2D.Obb, ShapeKind2D.Aabb):
                    return Collision2D.TryGetContact(a.Obb, b.Aabb, out contact);
                case (ShapeKind2D.Obb, ShapeKind2D.Obb):
                    return Collision2D.TryGetContact(a.Obb, b.Obb, out contact);
                default:
                    throw new ArgumentException("Unsupported world shape kind.");
            }
        }
    }
}
