using System;
using System.Collections.Generic;
using AshACT.Attributes;
using Framework.FixedPoint;

namespace AshACT.Effects
{
    public enum EffectArithmetic
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }

    /// <summary>不可变表达式树；工厂限制深度和节点数，求值只通过安全定点运算。</summary>
    public readonly struct EffectValue
    {
        public EffectValueKind Kind { get; }
        public FP64 Constant { get; }
        public uint ParameterId { get; }
        public AttributeId AttributeId { get; }
        public EffectExpression Expression { get; }

        public EffectValue(EffectValueKind kind, FP64 constant = default, uint parameterId = 0,
            AttributeId attributeId = default, EffectExpression expression = null)
        {
            Kind = kind;
            Constant = constant;
            ParameterId = parameterId;
            AttributeId = attributeId;
            Expression = expression;
        }

        public static EffectValue Arithmetic(EffectArithmetic operation, EffectValue left, EffectValue right)
        {
            return new EffectValue(EffectValueKind.Arithmetic, expression: new EffectExpression(operation, left, right));
        }

        internal EffectError Validate()
        {
            int count = 0;
            return Validate(0, ref count);
        }

        private EffectError Validate(int depth, ref int count)
        {
            count++;
            if (depth > 64 || count > 4096)
            {
                return EffectError.InvalidDefinition;
            }

            switch (Kind)
            {
                case EffectValueKind.Constant:
                    return AttributeMath.IsConfigurationValue(Constant) ? EffectError.None : EffectError.InvalidDefinition;
                case EffectValueKind.SetByCaller:
                    return ParameterId != 0 ? EffectError.None : EffectError.InvalidDefinition;
                case EffectValueKind.SourceBase:
                case EffectValueKind.SourceCurrent:
                case EffectValueKind.TargetBase:
                case EffectValueKind.TargetCurrent:
                    return AttributeId.IsValid ? EffectError.None : EffectError.UnsupportedValueSource;
                case EffectValueKind.Arithmetic:
                    if (Expression == null)
                    {
                        return EffectError.UnsupportedValueSource;
                    }

                    if (Expression.Operation < EffectArithmetic.Add || Expression.Operation > EffectArithmetic.Divide)
                    {
                        return EffectError.InvalidDefinition;
                    }

                    EffectError left = Expression.Left.Validate(depth + 1, ref count);
                    return left == EffectError.None ? Expression.Right.Validate(depth + 1, ref count) : left;
                default:
                    return EffectError.UnsupportedValueSource;
            }
        }

        internal IEnumerable<EffectValue> Leaves()
        {
            if (Kind == EffectValueKind.Arithmetic)
            {
                foreach (EffectValue value in Expression.Left.Leaves())
                {
                    yield return value;
                }

                foreach (EffectValue value in Expression.Right.Leaves())
                {
                    yield return value;
                }
            }
            else
            {
                yield return this;
            }
        }
    }

    public sealed class EffectExpression
    {
        public EffectArithmetic Operation { get; }
        public EffectValue Left { get; }
        public EffectValue Right { get; }

        public EffectExpression(EffectArithmetic operation, EffectValue left, EffectValue right)
        {
            Operation = operation;
            Left = left;
            Right = right;
        }
    }

    public enum AttributeReadKind
    {
        Base,
        Current
    }

    public readonly struct DependencyNode : IComparable<DependencyNode>, IEquatable<DependencyNode>
    {
        public EntityId EntityId { get; }
        public AttributeId AttributeId { get; }
        public AttributeReadKind Kind { get; }

        public DependencyNode(EntityId entityId, AttributeId attributeId, AttributeReadKind kind)
        {
            EntityId = entityId;
            AttributeId = attributeId;
            Kind = kind;
        }

        public int CompareTo(DependencyNode other)
        {
            int entity = EntityId.CompareTo(other.EntityId);
            if (entity != 0)
            {
                return entity;
            }

            int attribute = AttributeId.CompareTo(other.AttributeId);
            return attribute != 0 ? attribute : Kind.CompareTo(other.Kind);
        }

        public bool Equals(DependencyNode other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is DependencyNode other && Equals(other);
        public override int GetHashCode() => EntityId.GetHashCode() ^ AttributeId.GetHashCode() ^ (int)Kind;
    }

    /// <summary>按实体、属性、Base/Current 排序的稳定拓扑，不依赖哈希容器枚举顺序。</summary>
    internal sealed class DependencyGraph
    {
        private readonly SortedDictionary<DependencyNode, SortedSet<DependencyNode>> m_Edges =
            new SortedDictionary<DependencyNode, SortedSet<DependencyNode>>();

        internal void AddNode(DependencyNode node)
        {
            if (!m_Edges.ContainsKey(node))
            {
                m_Edges.Add(node, new SortedSet<DependencyNode>());
            }
        }

        internal void AddEdge(DependencyNode from, DependencyNode to)
        {
            AddNode(from);
            AddNode(to);
            m_Edges[from].Add(to);
        }

        internal bool TryOrder(out List<DependencyNode> order)
        {
            order = new List<DependencyNode>();
            SortedDictionary<DependencyNode, int> counts = new SortedDictionary<DependencyNode, int>();
            foreach (DependencyNode node in m_Edges.Keys)
            {
                counts.Add(node, 0);
            }

            foreach (SortedSet<DependencyNode> edges in m_Edges.Values)
            {
                foreach (DependencyNode node in edges)
                {
                    counts[node]++;
                }
            }

            SortedSet<DependencyNode> ready = new SortedSet<DependencyNode>();
            foreach (KeyValuePair<DependencyNode, int> pair in counts)
            {
                if (pair.Value == 0)
                {
                    ready.Add(pair.Key);
                }
            }

            while (ready.Count > 0)
            {
                DependencyNode node = ready.Min;
                ready.Remove(node);
                order.Add(node);
                foreach (DependencyNode next in m_Edges[node])
                {
                    counts[next]--;
                    if (counts[next] == 0)
                    {
                        ready.Add(next);
                    }
                }
            }

            return order.Count == m_Edges.Count;
        }
    }
}
