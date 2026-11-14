using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>不绑定 Body 身份的单圆平移扫掠命中。</summary>
    public readonly struct CircleSweepGeometryHit2D
    {
        public Fix64 Time { get; }
        public FixVector2 Center { get; }
        public FixVector2 Normal { get; }

        internal CircleSweepGeometryHit2D(Fix64 time, FixVector2 center, FixVector2 normal)
        {
            Time = time;
            Center = center;
            Normal = normal;
        }
    }
}
