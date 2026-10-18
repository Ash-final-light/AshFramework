using Framework.FixedPoint;

namespace AshACT.Attributes
{
    public enum AttributeOperation
    {
        Add,
        Multiply,
        Divide,
        Override
    }

    public enum AttributeCommandKind
    {
        Instant,
        AddPersistent,
        RemovePersistent
    }

    /// <summary>
    /// 第一阶段仅支持常量操作数。句柄是显式分配的非零编号，仅在所属属性容器内有效；
    /// 调用方须将移除命令发送到所属容器。应用顺序由属性容器在提交时确定。
    /// </summary>
    public readonly struct AttributeCommand
    {
        public AttributeCommandKind Kind { get; }
        public AttributeId AttributeId { get; }
        public AttributeOperation Operation { get; }
        public FP64 Operand { get; }
        public ulong ModifierHandle { get; }
        public int Priority { get; }

        public AttributeCommand(AttributeCommandKind kind, AttributeId attributeId,
            AttributeOperation operation = AttributeOperation.Add, FP64 operand = default,
            ulong modifierHandle = 0, int priority = 0)
        {
            Kind = kind;
            AttributeId = attributeId;
            Operation = operation;
            Operand = operand;
            ModifierHandle = modifierHandle;
            Priority = priority;
        }
    }
}
