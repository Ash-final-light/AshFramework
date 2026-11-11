using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>Minimum translation to the contact boundary for two overlapping shapes.</summary>
    public readonly struct Contact2D
    {
        /// <summary>Unit direction, rounded per component, for moving the second shape away from the first.</summary>
        public FixVector2 Normal
        {
            get;
        }

        /// <summary>Distance to the boundary, rounded up to a raw unit. Zero at tangency.</summary>
        public Fix64 Depth
        {
            get;
        }

        internal Contact2D(FixVector2 normal, Fix64 depth)
        {
            Normal = normal;
            Depth = depth;
        }

        internal Contact2D Reversed()
        {
            return new Contact2D(-Normal, Depth);
        }
    }
}
