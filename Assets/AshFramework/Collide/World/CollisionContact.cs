namespace AshFramework.Collide
{
    /// <summary>一次成功 Step 中的一条接触。子形状索引按 Pair.A、Pair.B 解释。</summary>
    public readonly struct CollisionContact
    {
        public CollisionPair Pair
        {
            get;
        }

        public CompoundContact2D Shapes
        {
            get;
        }

        internal CollisionContact(CollisionPair pair, CompoundContact2D shapes)
        {
            Pair = pair;
            Shapes = shapes;
        }
    }
}
