namespace AshFramework.Collide
{
    internal sealed class CollisionBodyRecord
    {
        internal readonly CompoundShape2D Geometry = new CompoundShape2D();
        internal CollisionBodyId Id;
        internal LogicalHeight Height;
        internal bool Active;
        internal bool Indexed;
        internal Aabb2D IndexedBounds;
        internal int RegisteredIndex;
        internal string Error = string.Empty;
    }
}
