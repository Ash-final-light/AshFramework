using System;

namespace AshFramework.Math
{
    /// <summary>依次执行缩放、逆时针旋转和平移；旋转角度使用弧度。</summary>
    [Serializable]
    public readonly struct FixTransform2D
    {
        public static readonly FixTransform2D Identity = new FixTransform2D(FixVector2.Zero, 0, FixVector2.One);

        public FixVector2 Position { get; }
        public Fix64 Rotation { get; }
        public FixVector2 Scale { get; }

        public FixTransform2D(FixVector2 position, Fix64 rotation, FixVector2 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public FixVector2 TransformPoint(FixVector2 point)
        {
            return TransformVector(point) + Position;
        }

        public FixVector2 TransformVector(FixVector2 vector)
        {
            return FixVector2.Scale(vector, Scale).Rotate(Rotation);
        }

        /// <summary>方向变换忽略缩放和平移。</summary>
        public FixVector2 TransformDirection(FixVector2 direction)
        {
            return direction.Rotate(Rotation);
        }

        public FixVector2 InverseTransformPoint(FixVector2 point)
        {
            return InverseTransformVector(point - Position);
        }

        public FixVector2 InverseTransformVector(FixVector2 vector)
        {
            if (Scale.X == Fix64.Zero || Scale.Y == Fix64.Zero)
            {
                throw new InvalidOperationException("Zero scale has no inverse.");
            }

            FixVector2 rotated = vector.Rotate(-Rotation);
            return new FixVector2(rotated.X / Scale.X, rotated.Y / Scale.Y);
        }

        public FixVector2 InverseTransformDirection(FixVector2 direction)
        {
            return direction.Rotate(-Rotation);
        }

        public FixMatrix3x3 ToMatrix()
        {
            FixMath.SinCos(Rotation, out Fix64 sin, out Fix64 cos);
            return new FixMatrix3x3(cos * Scale.X, -sin * Scale.Y, Position.X,
                sin * Scale.X, cos * Scale.Y, Position.Y, 0, 0, 1);
        }

        /// <summary>返回矩阵，以保留非均匀缩放与旋转组合时可能产生的剪切。</summary>
        public FixMatrix3x3 Compose(FixTransform2D child)
        {
            return ToMatrix() * child.ToMatrix();
        }
    }
}
