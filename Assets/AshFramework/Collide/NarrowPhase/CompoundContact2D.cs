namespace AshFramework.Collide
{
    /// <summary>一次检测中的子形状索引和接触。索引对应输入复合体的形状顺序。</summary>
    public readonly struct CompoundContact2D
    {
        public int ShapeIndexA
        {
            get;
        }

        public int ShapeIndexB
        {
            get;
        }

        public Contact2D Contact
        {
            get;
        }

        internal CompoundContact2D(int shapeIndexA, int shapeIndexB, Contact2D contact)
        {
            ShapeIndexA = shapeIndexA;
            ShapeIndexB = shapeIndexB;
            Contact = contact;
        }
    }
}
