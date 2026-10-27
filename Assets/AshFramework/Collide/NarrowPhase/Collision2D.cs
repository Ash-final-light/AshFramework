namespace AshFramework.Collide
{
    /// <summary>
    /// 纯二维布尔精筛。相切算相交；所有入口拒绝 default 形状。
    /// 包含六种无序配对及三个反向重载，不生成法线、穿透深度或事件。
    /// </summary>
    public static class Collision2D
    {
        public static bool Overlaps(Circle2D a, Circle2D b)
        {
            a.Validate();
            b.Validate();
            return GeometryPredicates.CircleCircle(a, b);
        }

        public static bool Overlaps(Circle2D circle, Aabb2D box)
        {
            circle.Validate();
            box.Validate();
            return GeometryPredicates.CircleAabb(circle, box);
        }

        public static bool Overlaps(Aabb2D box, Circle2D circle)
        {
            return Overlaps(circle, box);
        }

        public static bool Overlaps(Circle2D circle, Obb2D box)
        {
            circle.Validate();
            box.Validate();
            return GeometryPredicates.CircleObb(circle, box);
        }

        public static bool Overlaps(Obb2D box, Circle2D circle)
        {
            return Overlaps(circle, box);
        }

        public static bool Overlaps(Aabb2D a, Aabb2D b)
        {
            a.Validate();
            b.Validate();
            return GeometryPredicates.AabbAabb(a, b);
        }

        public static bool Overlaps(Aabb2D aabb, Obb2D obb)
        {
            aabb.Validate();
            obb.Validate();
            return GeometryPredicates.Boxes(new BoxData(aabb), new BoxData(obb));
        }

        public static bool Overlaps(Obb2D obb, Aabb2D aabb)
        {
            return Overlaps(aabb, obb);
        }

        public static bool Overlaps(Obb2D a, Obb2D b)
        {
            a.Validate();
            b.Validate();
            return GeometryPredicates.Boxes(new BoxData(a), new BoxData(b));
        }
    }
}
