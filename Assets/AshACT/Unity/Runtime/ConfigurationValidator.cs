using System;
using System.Collections.Generic;
using AshACT.Attributes;
using AshACT.Effects;
using Framework.FixedPoint;

namespace AshACT.Unity
{
    /// <summary>只检查作者期配置，不创建属性容器、效果定义或运行会话。</summary>
    public static class ConfigurationValidator
    {
        public const int MaximumEntries = 1024;
        public const int MaximumNodes = 512;
        public const int MaximumModifiers = 128;

        public static bool TryValidateCatalog(AttributeCatalogAsset catalog, out IReadOnlyList<ConfigurationIssue> issues)
        {
            Context context = new Context(catalog);
            context.ValidateCatalog();
            issues = context.Issues.AsReadOnly();
            return issues.Count == 0;
        }

        public static bool TryValidateProfile(AttributeCatalogAsset catalog, CharacterAttributeAsset profile,
            out IReadOnlyList<ConfigurationIssue> issues)
        {
            Context context = new Context(catalog);
            context.ValidateCatalog();
            if (context.Issues.Count == 0)
            {
                context.ValidateProfile(profile);
            }

            issues = context.Issues.AsReadOnly();
            return issues.Count == 0;
        }

        public static bool TryValidateEffect(AttributeCatalogAsset catalog, EffectGraphAsset effect,
            out IReadOnlyList<ConfigurationIssue> issues)
        {
            Context context = new Context(catalog);
            context.ValidateCatalog();
            if (context.Issues.Count == 0)
            {
                context.ValidateEffect(effect);
            }

            issues = context.Issues.AsReadOnly();
            return issues.Count == 0;
        }

        private readonly struct ConstantValue
        {
            internal bool Known { get; }
            internal FP64 Value { get; }
            internal AttributeError Error { get; }

            internal ConstantValue(FP64 value, AttributeError error = AttributeError.None)
            {
                Known = true;
                Value = value;
                Error = error;
            }
        }

        private sealed class Context
        {
            internal readonly List<ConfigurationIssue> Issues = new List<ConfigurationIssue>();
            private readonly AttributeCatalogAsset m_Catalog;
            private readonly Dictionary<uint, AttributeDefinitionAsset> m_Attributes = new Dictionary<uint, AttributeDefinitionAsset>();

            internal Context(AttributeCatalogAsset catalog)
            {
                m_Catalog = catalog;
            }

            internal void ValidateCatalog()
            {
                if (!Version(m_Catalog) || !List(m_Catalog, "Attributes", m_Catalog.Attributes))
                {
                    return;
                }

                foreach (AttributeDefinitionAsset attribute in m_Catalog.Attributes)
                {
                    if (!Version(attribute))
                    {
                        continue;
                    }

                    if (attribute.AttributeId == 0 || m_Attributes.ContainsKey(attribute.AttributeId))
                    {
                        Error(attribute, "AttributeId", "InvalidId", "属性编号必须非零且唯一。");
                        continue;
                    }

                    m_Attributes.Add(attribute.AttributeId, attribute);
                    FP64 min = Number(attribute, "Minimum", attribute.Minimum);
                    FP64 max = Number(attribute, "Maximum", attribute.Maximum);
                    if (min > max || !Enum.IsDefined(typeof(AttributeKind), attribute.Kind) ||
                        (attribute.Kind == AttributeKind.Stat && attribute.MaximumAttributeId != 0))
                    {
                        Error(attribute, "Kind/Bounds", "InvalidRange", "分类或属性默认边界无效。");
                    }
                }

                foreach (AttributeDefinitionAsset attribute in m_Attributes.Values)
                {
                    if (attribute.MaximumAttributeId != 0 &&
                        (!m_Attributes.TryGetValue(attribute.MaximumAttributeId, out AttributeDefinitionAsset maximum) ||
                        maximum.Kind != AttributeKind.Stat))
                    {
                        Error(attribute, "MaximumAttributeId", "MissingAttribute", "资源上限必须引用目录中的 Stat。");
                    }
                }
            }

            internal void ValidateProfile(CharacterAttributeAsset asset)
            {
                if (!Version(asset))
                {
                    return;
                }

                if (asset.ConfigurationId == 0)
                {
                    Error(asset, "ConfigurationId", "InvalidId", "角色配置编号必须非零。");
                }

                MarkerAuthoringValidation.TryValidateProfile(asset, out IReadOnlyList<ConfigurationIssue> markerIssues);
                Issues.AddRange(markerIssues);
                if (!List(asset, "Attributes", asset.Attributes))
                {
                    return;
                }

                Dictionary<uint, CharacterAttributeEntry> entries = new Dictionary<uint, CharacterAttributeEntry>();
                foreach (CharacterAttributeEntry entry in asset.Attributes)
                {
                    if (entry?.Definition != null && !entries.ContainsKey(entry.Definition.AttributeId))
                    {
                        entries.Add(entry.Definition.AttributeId, entry);
                    }
                }

                HashSet<uint> selected = new HashSet<uint>();
                for (int i = 0; i < asset.Attributes.Length; i++)
                {
                    CharacterAttributeEntry entry = asset.Attributes[i];
                    string path = $"Attributes[{i}]";
                    if (entry?.Definition == null ||
                        !m_Attributes.TryGetValue(entry.Definition.AttributeId, out AttributeDefinitionAsset definition) ||
                        definition != entry.Definition)
                    {
                        Error(asset, path, "MissingAttribute", "必须引用目录中的属性定义。");
                        continue;
                    }

                    if (!selected.Add(definition.AttributeId))
                    {
                        Error(asset, path, "DuplicateAttribute", "角色属性不能重复。");
                    }

                    FP64 initial = Number(asset, path + ".InitialBase", entry.InitialBase);
                    FP64 min = Number(asset, path + ".Minimum", entry.OverrideBounds ? entry.Minimum : definition.Minimum);
                    FP64 max = Number(asset, path + ".Maximum", entry.OverrideBounds ? entry.Maximum : definition.Maximum);
                    if (min > max || initial < min || initial > max)
                    {
                        Error(asset, path, "InvalidRange", "初值必须在角色边界内，下限不得超过上限。");
                    }

                    uint maximumId = entry.OverrideBounds ? entry.MaximumAttributeId : definition.MaximumAttributeId;
                    if (definition.Kind == AttributeKind.Stat && maximumId != 0)
                    {
                        Error(asset, path, "InvalidConfiguration", "Stat 不能引用资源上限。");
                    }

                    if (maximumId == 0)
                    {
                        continue;
                    }

                    if (!entries.TryGetValue(maximumId, out CharacterAttributeEntry maximum) ||
                        !m_Attributes.TryGetValue(maximumId, out AttributeDefinitionAsset maximumDefinition) ||
                        maximum.Definition != maximumDefinition)
                    {
                        Error(asset, path + ".MaximumAttributeId", "MissingAttribute", "资源上限必须由当前角色拥有。");
                    }
                    else if (maximumDefinition.Kind != AttributeKind.Stat)
                    {
                        Error(asset, path + ".MaximumAttributeId", "InvalidConfiguration", "资源上限必须引用 Stat。");
                    }
                    else if (Number(asset, path + ".MaximumAttributeId", maximum.InitialBase) < min)
                    {
                        Error(asset, path, "InvalidConfiguration", "资源的有效上限不得低于下限。");
                    }
                }
            }

            internal void ValidateEffect(EffectGraphAsset asset)
            {
                if (!Version(asset))
                {
                    return;
                }

                MarkerAuthoringValidation.TryValidateEffect(asset, null, out IReadOnlyList<ConfigurationIssue> markerIssues);
                Issues.AddRange(markerIssues);
                if (!List(asset, "Nodes", asset.Nodes, MaximumNodes) ||
                    !List(asset, "Modifiers", asset.Modifiers, MaximumModifiers) ||
                    !List(asset, "RequiredParameters", asset.RequiredParameters))
                {
                    return;
                }

                if (asset.EffectDefinitionId == 0)
                {
                    Error(asset, "EffectDefinitionId", "InvalidId", "效果编号必须非零。");
                }

                if (!Enum.IsDefined(typeof(EffectDurationKind), asset.DurationKind) || asset.PeriodFrames < 0 ||
                    (asset.DurationKind == EffectDurationKind.Finite ? asset.DurationFrames <= 0 : asset.DurationFrames != 0) ||
                    (asset.DurationKind == EffectDurationKind.Instant && asset.PeriodFrames != 0))
                {
                    Error(asset, "DurationKind/DurationFrames/PeriodFrames", "InvalidDefinition", "持续类型、持续帧或周期帧无效。");
                }

                HashSet<uint> parameters = new HashSet<uint>();
                foreach (uint id in asset.RequiredParameters)
                {
                    if (id == 0 || !parameters.Add(id))
                    {
                        Error(asset, "RequiredParameters", "InvalidParameter", "必填参数编号必须非零且唯一。");
                    }
                }

                Dictionary<int, EffectValueNode> nodes = new Dictionary<int, EffectValueNode>();
                foreach (EffectValueNode node in asset.Nodes)
                {
                    if (node == null || node.NodeId <= 0 || nodes.ContainsKey(node.NodeId))
                    {
                        Error(asset, "Nodes", "InvalidNodeId", "节点编号必须为正且唯一。");
                    }
                    else
                    {
                        nodes.Add(node.NodeId, node);
                    }
                }

                Dictionary<ulong, HashSet<ulong>> edges = new Dictionary<ulong, HashSet<ulong>>();
                foreach (uint id in m_Attributes.Keys)
                {
                    Edge(edges, (ulong)id * 2, (ulong)id * 2 + 1);
                }

                HashSet<int> used = new HashSet<int>();
                for (int i = 0; i < asset.Modifiers.Length; i++)
                {
                    EffectModifierNode modifier = asset.Modifiers[i];
                    string path = $"Modifiers[{i}]";
                    if (modifier == null)
                    {
                        Error(asset, path, "MissingModifier", "修改器不能为空。");
                        continue;
                    }

                    if (!Enum.IsDefined(typeof(AttributeOperation), modifier.Operation))
                    {
                        Error(asset, path + ".Operation", "InvalidDefinition", "属性修改操作无效。", modifier.ValueNodeId);
                    }

                    if (!m_Attributes.TryGetValue(modifier.AttributeId, out AttributeDefinitionAsset target))
                    {
                        Error(asset, path, "MissingAttribute", "被修改属性必须在目录中声明。", modifier.ValueNodeId);
                    }
                    else if (target.Kind == AttributeKind.Resource &&
                        asset.DurationKind != EffectDurationKind.Instant && asset.PeriodFrames == 0)
                    {
                        Error(asset, path, "ResourcePersistent", "资源不能接受持续修改器；请修改其上限 Stat 或使用周期。", modifier.ValueNodeId);
                    }

                    HashSet<int> reads = new HashSet<int>();
                    int count = 0;
                    ConstantValue value = Visit(asset, modifier.ValueNodeId, nodes, parameters, used,
                        new HashSet<int>(), reads, 0, ref count);
                    if (value.Known && (value.Error != AttributeError.None || !AttributeMath.IsConfigurationValue(value.Value) ||
                        (modifier.Operation == AttributeOperation.Multiply && value.Value < FP64.Zero) ||
                        (modifier.Operation == AttributeOperation.Divide && value.Value < FP64.One)))
                    {
                        Error(asset, path, "InvalidOperand", "静态数值计算溢出、除零或完整倍率/除数无效。", modifier.ValueNodeId);
                    }

                    foreach (int id in reads)
                    {
                        EffectValueNode read = nodes[id];
                        if (read.Kind == EffectValueKind.TargetBase || read.Kind == EffectValueKind.TargetCurrent)
                        {
                            Edge(edges, (ulong)read.AttributeId * 2 + (read.Kind == EffectValueKind.TargetCurrent ? 1UL : 0UL),
                                (ulong)modifier.AttributeId * 2 + 1);
                        }
                    }
                }

                foreach (int id in nodes.Keys)
                {
                    if (!used.Contains(id))
                    {
                        Error(asset, "Nodes", "DisconnectedNode", "节点未连接到任何修改器。", id);
                    }
                }

                if (Issues.Count == 0 && asset.DurationKind != EffectDurationKind.Instant && asset.PeriodFrames == 0)
                {
                    ValidateStaticCycle(asset, edges);
                }
            }

            private ConstantValue Visit(EffectGraphAsset asset, int id, Dictionary<int, EffectValueNode> nodes,
                HashSet<uint> parameters, HashSet<int> used, HashSet<int> active, HashSet<int> reads, int depth, ref int count)
            {
                count++;
                if (depth > 64 || count > 4096 || !nodes.TryGetValue(id, out EffectValueNode node) || !active.Add(id))
                {
                    Error(asset, "Nodes", "InvalidConnection", "连接缺失、存在环或表达式展开超过深度 64 / 4096 节点。", id);
                    return default;
                }

                used.Add(id);
                reads.Add(id);
                ConstantValue result = default;
                if (node.Kind == EffectValueKind.Arithmetic)
                {
                    ConstantValue left = Visit(asset, node.LeftNodeId, nodes, parameters, used, active, reads, depth + 1, ref count);
                    ConstantValue right = Visit(asset, node.RightNodeId, nodes, parameters, used, active, reads, depth + 1, ref count);
                    if (!Enum.IsDefined(typeof(EffectArithmetic), node.Arithmetic))
                    {
                        Error(asset, "Nodes", "InvalidDefinition", "四则运算类型无效。", id);
                    }
                    else if (node.Arithmetic == EffectArithmetic.Divide && right.Known &&
                        right.Error == AttributeError.None && right.Value.Raw == 0)
                    {
                        Error(asset, "Nodes", "StaticArithmetic", "常量除数不能为零。", id);
                    }

                    if (left.Known && right.Known)
                    {
                        result = Calculate(node.Arithmetic, left, right);
                        if (result.Error != AttributeError.None)
                        {
                            Error(asset, "Nodes", "StaticArithmetic", "常量子表达式溢出、操作无效或除数为零。", id);
                        }
                    }
                }
                else
                {
                    if (node.LeftNodeId != 0 || node.RightNodeId != 0)
                    {
                        Error(asset, "Nodes", "InvalidConnection", "只有四则运算节点允许输入连接。", id);
                    }

                    if (node.Kind == EffectValueKind.Constant)
                    {
                        result = new ConstantValue(Number(asset, $"Nodes[{id}].Constant", node.Constant, id));
                    }
                    else if (node.Kind == EffectValueKind.SetByCaller)
                    {
                        if (!parameters.Contains(node.ParameterId))
                        {
                            Error(asset, "RequiredParameters", "MissingParameter", "SetByCaller 必须声明为必填参数。", id);
                        }
                    }
                    else if (node.Kind >= EffectValueKind.SourceBase && node.Kind <= EffectValueKind.TargetCurrent)
                    {
                        if (!m_Attributes.ContainsKey(node.AttributeId))
                        {
                            Error(asset, "Nodes", "MissingAttribute", "读取属性必须在通用属性库中声明。", id);
                        }
                    }
                    else
                    {
                        Error(asset, "Nodes", "UnsupportedValueSource", "数值来源类型无效。", id);
                    }
                }

                active.Remove(id);
                return result;
            }

            private void ValidateStaticCycle(EffectGraphAsset asset, Dictionary<ulong, HashSet<ulong>> edges)
            {
                Dictionary<ulong, int> counts = new Dictionary<ulong, int>();
                foreach (ulong node in edges.Keys)
                {
                    counts.Add(node, 0);
                }

                foreach (HashSet<ulong> destinations in edges.Values)
                {
                    foreach (ulong destination in destinations)
                    {
                        counts[destination]++;
                    }
                }

                Queue<ulong> ready = new Queue<ulong>();
                foreach (KeyValuePair<ulong, int> pair in counts)
                {
                    if (pair.Value == 0)
                    {
                        ready.Enqueue(pair.Key);
                    }
                }

                int visited = 0;
                while (ready.Count != 0)
                {
                    ulong node = ready.Dequeue();
                    visited++;
                    foreach (ulong destination in edges[node])
                    {
                        counts[destination]--;
                        if (counts[destination] == 0)
                        {
                            ready.Enqueue(destination);
                        }
                    }
                }

                if (visited != edges.Count)
                {
                    Error(asset, "Modifiers", "DependencyCycle", "持续读取与目标属性构成静态依赖环。");
                }
            }

            private bool Version(VersionedConfigurationAsset asset)
            {
                if (asset == null || asset.SchemaVersion != VersionedConfigurationAsset.CurrentSchemaVersion || asset.ReleaseVersion <= 0 ||
                    (m_Catalog != null && asset.ReleaseVersion != m_Catalog.ReleaseVersion))
                {
                    Error(asset, "SchemaVersion/ReleaseVersion", "VersionMismatch", $"要求协议版本 {VersionedConfigurationAsset.CurrentSchemaVersion}，且资产发布版本与通用属性库一致。");
                    return false;
                }

                return true;
            }

            private bool List<T>(VersionedConfigurationAsset asset, string path, T[] entries, int limit = MaximumEntries)
            {
                if (entries == null || entries.Length > limit)
                {
                    Error(asset, path, "CollectionLimit", $"集合不能为空引用且最多允许 {limit} 项。");
                    return false;
                }

                return true;
            }

            private FP64 Number(VersionedConfigurationAsset asset, string path, string text, int nodeId = 0)
            {
                if (!DecimalValue.TryParse(text, out FP64 value))
                {
                    Error(asset, path, "InvalidDecimal", "要求最多四位小数的十进制文本，范围为 ±1000000000。", nodeId);
                }

                return value;
            }

            private void Error(VersionedConfigurationAsset asset, string path, string code, string message, int nodeId = 0)
            {
                Issues.Add(new ConfigurationIssue(code, asset, path, message, nodeId));
            }
        }

        private static void Edge(Dictionary<ulong, HashSet<ulong>> edges, ulong from, ulong to)
        {
            if (!edges.ContainsKey(from))
            {
                edges.Add(from, new HashSet<ulong>());
            }

            if (!edges.ContainsKey(to))
            {
                edges.Add(to, new HashSet<ulong>());
            }

            edges[from].Add(to);
        }

        private static ConstantValue Calculate(EffectArithmetic operation, ConstantValue left, ConstantValue right)
        {
            if (left.Error != AttributeError.None || right.Error != AttributeError.None)
            {
                return new ConstantValue(default, left.Error != AttributeError.None ? left.Error : right.Error);
            }

            FP64 value = default;
            AttributeError error;
            switch (operation)
            {
                case EffectArithmetic.Add:
                    error = AttributeMath.Add(left.Value, right.Value, out value);
                    break;
                case EffectArithmetic.Subtract:
                    error = AttributeMath.Subtract(left.Value, right.Value, out value);
                    break;
                case EffectArithmetic.Multiply:
                    error = AttributeMath.Multiply(left.Value, right.Value, out value);
                    break;
                case EffectArithmetic.Divide:
                    error = AttributeMath.Divide(left.Value, right.Value, out value);
                    break;
                default:
                    error = AttributeError.InvalidOperation;
                    break;
            }

            return new ConstantValue(value, error);
        }
    }
}
