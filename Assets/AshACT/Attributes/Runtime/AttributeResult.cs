namespace AshACT.Attributes
{
    public enum AttributeError
    {
        None,
        InvalidConfiguration,
        InvalidAttributeId,
        DuplicateAttribute,
        MissingAttribute,
        InvalidOperation,
        InvalidOperand,
        DivideByZero,
        Overflow,
        InvalidModifierHandle,
        DuplicateModifier,
        MissingModifier,
        ResourceModifierNotAllowed
    }

    /// <summary>确定性的执行结果；初始化失败时 CommandIndex 为 -1。</summary>
    public readonly struct AttributeResult
    {
        public AttributeError Error { get; }
        public AttributeId AttributeId { get; }
        public int CommandIndex { get; }
        public bool Succeeded => Error == AttributeError.None;

        internal AttributeResult(AttributeError error, AttributeId attributeId = default, int commandIndex = -1)
        {
            Error = error;
            AttributeId = attributeId;
            CommandIndex = commandIndex;
        }
    }
}
