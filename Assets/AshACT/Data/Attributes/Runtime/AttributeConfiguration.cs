using Framework.FixedPoint;

namespace AshACT.Attributes
{
    public enum AttributeKind
    {
        Stat,
        Resource
    }

    /// <summary>每个角色的不可变初始配置；MaximumAttribute 可引用同一属性容器内的数值属性。</summary>
    public readonly struct AttributeConfiguration
    {
        public AttributeId Id { get; }
        public AttributeKind Kind { get; }
        public FP64 InitialBaseValue { get; }
        public FP64 Minimum { get; }
        public FP64 Maximum { get; }
        public AttributeId MaximumAttribute { get; }

        public AttributeConfiguration(AttributeId id, FP64 initialBaseValue, FP64 minimum, FP64 maximum,
            AttributeKind kind = AttributeKind.Stat, AttributeId maximumAttribute = default)
        {
            Id = id;
            InitialBaseValue = initialBaseValue;
            Minimum = minimum;
            Maximum = maximum;
            Kind = kind;
            MaximumAttribute = maximumAttribute;
        }
    }

    public readonly struct AttributeValue
    {
        public FP64 BaseValue { get; }
        public FP64 CurrentValue { get; }

        internal AttributeValue(FP64 baseValue, FP64 currentValue)
        {
            BaseValue = baseValue;
            CurrentValue = currentValue;
        }
    }
}
