namespace AshFramework.Math
{
    /// <summary>
    /// 按明确顺序的小端数据计算 FNV-1a；字段和对象顺序由调用者定义。
    /// 仅用作诊断校验；哈希相等不能证明状态完全相同。
    /// </summary>
    public sealed class StableHash64
    {
        public const ulong InitialValue = 14695981039346656037UL;
        public ulong Value { get; private set; } = InitialValue;

        public void Reset()
        {
            Value = InitialValue;
        }

        public void AddByte(byte value)
        {
            Value = unchecked((Value ^ value) * 1099511628211UL);
        }

        public void AddUInt64(ulong value)
        {
            for (int index = 0; index < 8; index++)
            {
                AddByte(unchecked((byte)(value >> (index * 8))));
            }
        }

        public void AddInt64(long value)
        {
            AddUInt64(unchecked((ulong)value));
        }

        public void Add(Fix64 value)
        {
            AddInt64(value.RawValue);
        }

        public void Add(FixVector2 value)
        {
            Add(value.X);
            Add(value.Y);
        }

        public void Add(FixVector3 value)
        {
            Add(value.X);
            Add(value.Y);
            Add(value.Z);
        }

        public void Add(FixQuaternion value)
        {
            Add(value.X);
            Add(value.Y);
            Add(value.Z);
            Add(value.W);
        }
    }
}
