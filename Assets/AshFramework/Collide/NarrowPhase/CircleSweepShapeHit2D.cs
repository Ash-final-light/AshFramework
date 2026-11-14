using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>复合固定障碍中一个最早命中子形状的几何结果。</summary>
    public readonly struct CircleSweepShapeHit2D
    {
        public int ShapeIndex { get; }
        public Fix64 Time { get; }
        public FixVector2 Center { get; }
        public FixVector2 Normal { get; }

        internal CircleSweepShapeHit2D(int shapeIndex, CircleSweepGeometryHit2D hit)
        {
            ShapeIndex = shapeIndex;
            Time = hit.Time;
            Center = hit.Center;
            Normal = hit.Normal;
        }
    }
}
