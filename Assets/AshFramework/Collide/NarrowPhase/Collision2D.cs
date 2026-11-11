namespace AshFramework.Collide
{
    /// <summary>
    /// 纯二维精筛，提供布尔重叠与接触方向和深度。相切算相交；所有入口拒绝 default 形状。
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

        public static bool TryGetContact(Circle2D a, Circle2D b, out Contact2D contact)
        {
            a.Validate();
            b.Validate();
            return ContactGeometry2D.CircleCircle(a, b, out contact);
        }

        public static bool TryGetContact(Circle2D circle, Aabb2D box, out Contact2D contact)
        {
            circle.Validate();
            box.Validate();
            return ContactGeometry2D.CircleBox(circle, new BoxData(box), out contact);
        }

        public static bool TryGetContact(Aabb2D box, Circle2D circle, out Contact2D contact)
        {
            if (!TryGetContact(circle, box, out contact))
            {
                return false;
            }

            contact = contact.Reversed();
            return true;
        }

        public static bool TryGetContact(Circle2D circle, Obb2D box, out Contact2D contact)
        {
            circle.Validate();
            box.Validate();
            return ContactGeometry2D.CircleBox(circle, new BoxData(box), out contact);
        }

        public static bool TryGetContact(Obb2D box, Circle2D circle, out Contact2D contact)
        {
            if (!TryGetContact(circle, box, out contact))
            {
                return false;
            }

            contact = contact.Reversed();
            return true;
        }

        public static bool TryGetContact(Aabb2D a, Aabb2D b, out Contact2D contact)
        {
            a.Validate();
            b.Validate();
            return ContactGeometry2D.AabbAabb(a, b, out contact);
        }

        public static bool TryGetContact(Aabb2D aabb, Obb2D obb, out Contact2D contact)
        {
            aabb.Validate();
            obb.Validate();
            return ContactGeometry2D.Boxes(new BoxData(aabb), new BoxData(obb), out contact);
        }

        public static bool TryGetContact(Obb2D obb, Aabb2D aabb, out Contact2D contact)
        {
            if (!TryGetContact(aabb, obb, out contact))
            {
                return false;
            }

            contact = contact.Reversed();
            return true;
        }

        public static bool TryGetContact(Obb2D a, Obb2D b, out Contact2D contact)
        {
            a.Validate();
            b.Validate();
            return ContactGeometry2D.Boxes(new BoxData(a), new BoxData(b), out contact);
        }
    }
}
