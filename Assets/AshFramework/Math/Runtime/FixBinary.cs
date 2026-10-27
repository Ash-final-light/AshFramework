using System;

namespace AshFramework.Math
{
    /// <summary>第 1 版 Q32.32 原始值编码；明确采用小端字节序，不含填充、头部或反射。</summary>
    public static class FixBinary
    {
        public const int ScalarSize = 8;
        public const int Vector2Size = 16;
        public const int Vector3Size = 24;
        public const int QuaternionSize = 32;

        public static void WriteInt64(byte[] buffer, int offset, long value)
        {
            Validate(buffer, offset, ScalarSize);
            ulong bits = unchecked((ulong)value);
            for (int index = 0; index < ScalarSize; index++)
            {
                buffer[offset + index] = unchecked((byte)(bits >> (index * 8)));
            }
        }

        public static long ReadInt64(byte[] buffer, int offset)
        {
            Validate(buffer, offset, ScalarSize);
            ulong bits = 0;
            for (int index = 0; index < ScalarSize; index++)
            {
                bits |= (ulong)buffer[offset + index] << (index * 8);
            }

            return unchecked((long)bits);
        }

        public static void Write(byte[] buffer, int offset, Fix64 value)
        {
            WriteInt64(buffer, offset, value.RawValue);
        }

        public static Fix64 ReadFix64(byte[] buffer, int offset)
        {
            return Fix64.FromRaw(ReadInt64(buffer, offset));
        }

        public static void Write(byte[] buffer, int offset, FixVector2 value)
        {
            Validate(buffer, offset, Vector2Size);
            Write(buffer, offset, value.X);
            Write(buffer, offset + ScalarSize, value.Y);
        }

        public static FixVector2 ReadVector2(byte[] buffer, int offset)
        {
            Validate(buffer, offset, Vector2Size);
            return new FixVector2(ReadFix64(buffer, offset), ReadFix64(buffer, offset + ScalarSize));
        }

        public static void Write(byte[] buffer, int offset, FixVector3 value)
        {
            Validate(buffer, offset, Vector3Size);
            Write(buffer, offset, value.X);
            Write(buffer, offset + ScalarSize, value.Y);
            Write(buffer, offset + ScalarSize * 2, value.Z);
        }

        public static FixVector3 ReadVector3(byte[] buffer, int offset)
        {
            Validate(buffer, offset, Vector3Size);
            return new FixVector3(ReadFix64(buffer, offset), ReadFix64(buffer, offset + ScalarSize),
                ReadFix64(buffer, offset + ScalarSize * 2));
        }

        public static void Write(byte[] buffer, int offset, FixQuaternion value)
        {
            Validate(buffer, offset, QuaternionSize);
            Write(buffer, offset, value.X);
            Write(buffer, offset + ScalarSize, value.Y);
            Write(buffer, offset + ScalarSize * 2, value.Z);
            Write(buffer, offset + ScalarSize * 3, value.W);
        }

        public static FixQuaternion ReadQuaternion(byte[] buffer, int offset)
        {
            Validate(buffer, offset, QuaternionSize);
            return new FixQuaternion(ReadFix64(buffer, offset), ReadFix64(buffer, offset + ScalarSize),
                ReadFix64(buffer, offset + ScalarSize * 2), ReadFix64(buffer, offset + ScalarSize * 3));
        }

        private static void Validate(byte[] buffer, int offset, int size)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || offset > buffer.Length - size)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }
    }
}
