using System;
using System.Collections.Generic;
using AshACT.Attributes;
using AshACT.Effects;
using Framework.FixedPoint;

namespace AshACT.Unity
{
    public static class ConfigurationCompiler
    {
        public const int MaximumEntries = 1024;
        public const int MaximumNodes = 512;
        public const int MaximumModifiers = 128;

        public static bool TryCompile(AttributeCatalogAsset asset, out CompiledAttributeCatalog catalog,
            out IReadOnlyList<ConfigurationIssue> issues)
        {
            Context context = new Context(asset);
            catalog = context.Compile();
            issues = context.Issues.AsReadOnly();
            return catalog != null;
        }

        public static bool TryCompileProfile(AttributeCatalogAsset catalog, CharacterAttributeAsset profile,
            out CompiledProfile compiled, out IReadOnlyList<ConfigurationIssue> issues)
        {
            Context context = new Context(catalog);
            compiled = context.SingleProfile(profile);
            issues = context.Issues.AsReadOnly();
            return compiled != null;
        }

        public static bool TryCompileEffect(AttributeCatalogAsset catalog, EffectGraphAsset effect,
            out CompiledEffect compiled, out IReadOnlyList<ConfigurationIssue> issues)
        {
            Context context = new Context(catalog);
            compiled = context.SingleEffect(effect);
            issues = context.Issues.AsReadOnly();
            return compiled != null;
        }

        public static bool TryCompileSession(AttributeSessionSetup setup, out CompiledSessionConfiguration compiled,
            out IReadOnlyList<ConfigurationIssue> issues)
        {
            if (setup == null)
            {
                compiled = null;
                issues = Array.AsReadOnly(new[]
                {
                    new ConfigurationIssue("MissingSetup", null, "SessionSetup", "运行方必须提供本次初始化数据。")
                });
                return false;
            }

            return TryCompileSession(setup.Catalog, setup.Entities, setup.Effects, setup.Markers, out compiled, out issues);
        }

        public static bool TryCompileSession(AttributeCatalogAsset catalog, EntityConfiguration[] entities,
            EffectGraphAsset[] effects, MarkerConfiguration[] markers, out CompiledSessionConfiguration compiled,
            out IReadOnlyList<ConfigurationIssue> issues)
        {
            Context context = new Context(catalog);
            compiled = context.CompileSession(entities, effects, markers);
            issues = context.Issues.AsReadOnly();
            return compiled != null;
        }

        private sealed class Context
        {
            internal readonly List<ConfigurationIssue> Issues = new List<ConfigurationIssue>();
            private readonly AttributeCatalogAsset m_Catalog;
            private readonly Dictionary<uint, AttributeDefinitionAsset> m_Attributes = new Dictionary<uint, AttributeDefinitionAsset>();
            private readonly Dictionary<uint, CharacterAttributeAsset> m_ProfileAssets = new Dictionary<uint, CharacterAttributeAsset>();
            private readonly Dictionary<uint, CompiledProfile> m_Profiles = new Dictionary<uint, CompiledProfile>();
            private readonly Dictionary<uint, CompiledEffect> m_Effects = new Dictionary<uint, CompiledEffect>();
            private readonly Dictionary<ulong, uint> m_Entities = new Dictionary<ulong, uint>();
            private readonly HashSet<uint> m_Markers = new HashSet<uint>();

            internal Context(AttributeCatalogAsset catalog)
            {
                m_Catalog = catalog;
            }

            internal CompiledAttributeCatalog Compile()
            {
                if (!Version(m_Catalog) || !List(m_Catalog, "Attributes", m_Catalog.Attributes))
                {
                    return null;
                }

                Dictionary<uint, CompiledAttributeDefinition> definitions = new Dictionary<uint, CompiledAttributeDefinition>();
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
                    definitions.Add(attribute.AttributeId, new CompiledAttributeDefinition(attribute, min, max));
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

                return Issues.Count == 0 ? new CompiledAttributeCatalog(m_Catalog.ReleaseVersion, definitions) : null;
            }

            internal CompiledProfile SingleProfile(CharacterAttributeAsset profile)
            {
                if (Compile() == null)
                {
                    return null;
                }

                CompileProfile(profile);
                return Issues.Count == 0 ? m_Profiles[profile.ConfigurationId] : null;
            }

            internal CompiledEffect SingleEffect(EffectGraphAsset effect)
            {
                if (Compile() == null || !Version(effect))
                {
                    return null;
                }

                CompileEffect(effect);
                return Issues.Count == 0 ? m_Effects[effect.EffectDefinitionId] : null;
            }

            internal CompiledSessionConfiguration CompileSession(EntityConfiguration[] entities,
                EffectGraphAsset[] effects, MarkerConfiguration[] markers)
            {
                CompiledAttributeCatalog catalog = Compile();
                VersionedConfigurationAsset owner = m_Catalog;
                if (catalog == null || !List(owner, "SessionSetup.Entities", entities) ||
                    !List(owner, "SessionSetup.Effects", effects) || !List(owner, "SessionSetup.Markers", markers))
                {
                    return null;
                }

                foreach (MarkerConfiguration marker in markers)
                {
                    if (marker == null || marker.MarkerId == 0 || !m_Markers.Add(marker.MarkerId))
                    {
                        Error(owner, "SessionSetup.Markers", "InvalidId", "标记编号必须非零且唯一。");
                    }
                }

                for (int i = 0; i < entities.Length; i++)
                {
                    EntityConfiguration entity = entities[i];
                    string path = $"SessionSetup.Entities[{i}]";
                    if (entity == null || entity.EntityId == 0 || m_Entities.ContainsKey(entity.EntityId))
                    {
                        Error(owner, path, "InvalidId", "实体编号必须非零且唯一。");
                        continue;
                    }

                    CharacterAttributeAsset profile = entity.Profile;
                    if (profile == null || !m_ProfileAssets.TryGetValue(profile.ConfigurationId, out CharacterAttributeAsset existing) || existing != profile)
                    {
                        CompileProfile(profile);
                    }

                    if (!Profile(profile))
                    {
                        Error(owner, path, "MissingProfile", "实体必须提供通过属性库校验的角色属性配置。");
                    }
                    else
                    {
                        m_Entities.Add(entity.EntityId, profile.ConfigurationId);
                    }
                }

                HashSet<uint> effectIds = new HashSet<uint>();
                foreach (EffectGraphAsset effect in effects)
                {
                    if (!Version(effect))
                    {
                        continue;
                    }

                    if (effect.EffectDefinitionId == 0 || !effectIds.Add(effect.EffectDefinitionId))
                    {
                        Error(effect, "EffectDefinitionId", "InvalidId", "效果编号必须非零且唯一。");
                        continue;
                    }

                    CompileEffect(effect);
                    if (effect.MarkerIds != null)
                    {
                        for (int i = 0; i < effect.MarkerIds.Length; i++)
                        {
                            if (!m_Markers.Contains(effect.MarkerIds[i]))
                            {
                                Error(effect, $"MarkerIds[{i}]", "MissingMarker", "标记必须在本次初始化清单中声明。");
                            }
                        }
                    }
                }

                return Issues.Count == 0 ? new CompiledSessionConfiguration(catalog, m_Profiles, m_Effects, m_Entities) : null;
            }

            private void CompileProfile(CharacterAttributeAsset asset)
            {
                if (!Version(asset))
                {
                    return;
                }

                if (asset.ConfigurationId == 0 || m_ProfileAssets.ContainsKey(asset.ConfigurationId))
                {
                    Error(asset, "ConfigurationId", "InvalidId", "角色配置编号必须非零且唯一。");
                    return;
                }

                m_ProfileAssets.Add(asset.ConfigurationId, asset);
                if (!List(asset, "Attributes", asset.Attributes))
                {
                    return;
                }

                int before = Issues.Count;
                List<AttributeConfiguration> entries = new List<AttributeConfiguration>();
                for (int i = 0; i < asset.Attributes.Length; i++)
                {
                    CharacterAttributeEntry entry = asset.Attributes[i];
                    string path = $"Attributes[{i}]";
                    if (entry == null || entry.Definition == null ||
                        !m_Attributes.TryGetValue(entry.Definition.AttributeId, out AttributeDefinitionAsset definition) ||
                        definition != entry.Definition)
                    {
                        Error(asset, path, "MissingAttribute", "必须引用目录中的属性定义。");
                        continue;
                    }

                    FP64 initial = Number(asset, path + ".InitialBase", entry.InitialBase);
                    FP64 min = Number(asset, path + ".Minimum", entry.OverrideBounds ? entry.Minimum : definition.Minimum);
                    FP64 max = Number(asset, path + ".Maximum", entry.OverrideBounds ? entry.Maximum : definition.Maximum);
                    if (min > max || initial < min || initial > max)
                    {
                        Error(asset, path, "InvalidRange", "初值必须在角色边界内，下限不得超过上限。");
                    }

                    entries.Add(new AttributeConfiguration(new AttributeId(definition.AttributeId), initial, min, max,
                        definition.Kind, new AttributeId(entry.OverrideBounds ? entry.MaximumAttributeId : definition.MaximumAttributeId)));
                }

                AttributeResult result = AttributeStore.TryCreate(entries, out AttributeStore store);
                if (!result.Succeeded)
                {
                    Error(asset, "Attributes", result.Error.ToString(), $"属性 {result.AttributeId} 的集合或资源上限无效。");
                }

                if (Issues.Count == before)
                {
                    entries.Sort((a, b) => a.Id.CompareTo(b.Id));
                    m_Profiles.Add(asset.ConfigurationId, new CompiledProfile(asset.ConfigurationId, entries.ToArray()));
                }
            }

            private void CompileEffect(EffectGraphAsset asset)
            {
                int before = Issues.Count;
                if (!List(asset, "Nodes", asset.Nodes, MaximumNodes) ||
                    !List(asset, "Modifiers", asset.Modifiers, MaximumModifiers) ||
                    !List(asset, "MarkerIds", asset.MarkerIds) || !List(asset, "RequiredParameters", asset.RequiredParameters))
                {
                    return;
                }

                if (asset.EffectDefinitionId == 0)
                {
                    Error(asset, "EffectDefinitionId", "InvalidId", "效果编号必须非零。");
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

                List<EffectModifier> modifiers = new List<EffectModifier>();
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

                    if (!m_Attributes.TryGetValue(modifier.AttributeId, out AttributeDefinitionAsset target))
                    {
                        Error(asset, path, "MissingAttribute", "被修改属性必须在目录中声明；目标是否拥有它在施加时校验。", modifier.ValueNodeId);
                    }
                    else if (target.Kind == AttributeKind.Resource &&
                        asset.DurationKind != EffectDurationKind.Instant && asset.PeriodFrames == 0)
                    {
                        Error(asset, path, "ResourcePersistent", "资源不能接受持续修改器；请修改其上限 Stat 或使用周期。", modifier.ValueNodeId);
                    }

                    int count = 0;
                    EffectValue value = Build(asset, modifier.ValueNodeId, nodes, parameters, used,
                        new HashSet<int>(), 0, ref count);
                    if (TryConstant(value, out FP64 constant, out AttributeError arithmeticError))
                    {
                        if (arithmeticError != AttributeError.None || !AttributeMath.IsConfigurationValue(constant) ||
                            (modifier.Operation == AttributeOperation.Multiply && constant < FP64.Zero) ||
                            (modifier.Operation == AttributeOperation.Divide && constant < FP64.One))
                        {
                            Error(asset, path, "InvalidOperand", "静态数值计算溢出、除零或完整倍率/除数无效。", modifier.ValueNodeId);
                        }
                    }

                    modifiers.Add(new EffectModifier(new AttributeId(modifier.AttributeId), modifier.Operation, value, modifier.Priority));
                }

                foreach (int id in nodes.Keys)
                {
                    if (!used.Contains(id))
                    {
                        Error(asset, "Nodes", "DisconnectedNode", "节点未连接到任何修改器。", id);
                    }
                }

                MarkerId[] markers = new MarkerId[asset.MarkerIds.Length];
                HashSet<uint> markerIds = new HashSet<uint>();
                for (int i = 0; i < markers.Length; i++)
                {
                    markers[i] = new MarkerId(asset.MarkerIds[i]);
                    if (!markerIds.Add(asset.MarkerIds[i]))
                    {
                        Error(asset, $"MarkerIds[{i}]", "DuplicateMarker", "同一效果不能重复声明相同标记。");
                    }
                }

                EffectResult result = EffectDefinition.TryCreate(new EffectDefinitionId(asset.EffectDefinitionId),
                    asset.DurationKind, asset.DurationFrames, asset.PeriodFrames, markers, modifiers, out EffectDefinition definition);
                if (!result.Succeeded)
                {
                    Error(asset, $"Modifiers[{result.ModifierIndex}]/Duration", result.Error.ToString(), "运行定义工厂拒绝该配置。");
                }

                if (Issues.Count == before)
                {
                    ValidateStaticCycle(asset, definition);
                }

                if (Issues.Count == before)
                {
                    m_Effects.Add(asset.EffectDefinitionId, new CompiledEffect(definition, asset.RequiredParameters));
                }
            }

            private EffectValue Build(EffectGraphAsset asset, int id, Dictionary<int, EffectValueNode> nodes,
                HashSet<uint> parameters, HashSet<int> used, HashSet<int> active, int depth, ref int count)
            {
                count++;
                if (depth > 64 || count > 4096 || !nodes.TryGetValue(id, out EffectValueNode node) || !active.Add(id))
                {
                    Error(asset, "Nodes", "InvalidConnection", "连接缺失、存在环或表达式展开超过深度 64 / 4096 节点。", id);
                    return default;
                }

                used.Add(id);
                EffectValue result;
                if (node.Kind == EffectValueKind.Arithmetic)
                {
                    EffectValue left = Build(asset, node.LeftNodeId, nodes, parameters, used, active, depth + 1, ref count);
                    EffectValue right = Build(asset, node.RightNodeId, nodes, parameters, used, active, depth + 1, ref count);
                    result = EffectValue.Arithmetic(node.Arithmetic, left, right);
                    bool constant = TryConstant(result, out FP64 ignored, out AttributeError arithmeticError);
                    bool zeroDivisor = node.Arithmetic == EffectArithmetic.Divide &&
                        TryConstant(right, out FP64 divisor, out AttributeError divisorError) &&
                        divisorError == AttributeError.None && divisor.Raw == 0;
                    if ((constant && arithmeticError != AttributeError.None) || zeroDivisor)
                    {
                        Error(asset, "Nodes", "StaticArithmetic", "常量子表达式溢出、操作无效或除数确定为零。", id);
                    }
                }
                else
                {
                    if (node.LeftNodeId != 0 || node.RightNodeId != 0)
                    {
                        Error(asset, "Nodes", "InvalidConnection", "只有四则运算节点允许输入连接。", id);
                    }

                    FP64 constant = default;
                    if (node.Kind == EffectValueKind.Constant)
                    {
                        constant = Number(asset, $"Nodes[{id}].Constant", node.Constant, id);
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
                            Error(asset, "Nodes", "MissingAttribute", "读取属性必须在通用属性库中声明；角色是否拥有该属性在施加时检查。", id);
                        }
                    }

                    result = new EffectValue(node.Kind, constant, node.ParameterId, new AttributeId(node.AttributeId));
                }

                active.Remove(id);
                return result;
            }

            private void ValidateStaticCycle(EffectGraphAsset asset, EffectDefinition definition)
            {
                if (definition.DurationKind == EffectDurationKind.Instant || definition.PeriodFrames != 0)
                {
                    return;
                }

                // 目标由调用方决定；这里只检查与角色无关的必然依赖，角色专属资源上限边在施加时检查。
                Dictionary<ulong, HashSet<ulong>> edges = new Dictionary<ulong, HashSet<ulong>>();
                foreach (uint attributeId in m_Attributes.Keys)
                {
                    ulong baseNode = (ulong)attributeId * 2;
                    Edge(edges, baseNode, baseNode + 1);
                }

                foreach (EffectModifier modifier in definition.Modifiers)
                {
                    AddReads(edges, modifier.Value, (ulong)modifier.AttributeId.Value * 2 + 1);
                }

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

            private static void AddReads(Dictionary<ulong, HashSet<ulong>> edges, EffectValue value, ulong destination)
            {
                if (value.Kind == EffectValueKind.Arithmetic)
                {
                    AddReads(edges, value.Expression.Left, destination);
                    AddReads(edges, value.Expression.Right, destination);
                }
                else if (value.Kind == EffectValueKind.TargetBase || value.Kind == EffectValueKind.TargetCurrent)
                {
                    Edge(edges, (ulong)value.AttributeId.Value * 2 + (value.Kind == EffectValueKind.TargetCurrent ? 1UL : 0UL), destination);
                }
            }

            private bool Profile(CharacterAttributeAsset profile)
            {
                return profile != null && m_ProfileAssets.TryGetValue(profile.ConfigurationId, out CharacterAttributeAsset found) &&
                    found == profile && m_Profiles.ContainsKey(profile.ConfigurationId);
            }

            private bool Version(VersionedConfigurationAsset asset)
            {
                if (asset == null || asset.SchemaVersion != VersionedConfigurationAsset.CurrentSchemaVersion || asset.ReleaseVersion <= 0 ||
                    (m_Catalog != null && asset.ReleaseVersion != m_Catalog.ReleaseVersion))
                {
                    Error(asset, "SchemaVersion/ReleaseVersion", "VersionMismatch", "要求协议版本 2，且参与编译的资产发布版本与通用属性库一致。");
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

        private static bool TryConstant(EffectValue value, out FP64 result, out AttributeError error)
        {
            result = value.Constant;
            error = AttributeError.None;
            if (value.Kind == EffectValueKind.Constant)
            {
                return true;
            }

            if (value.Kind != EffectValueKind.Arithmetic)
            {
                return false;
            }

            bool a = TryConstant(value.Expression.Left, out FP64 left, out AttributeError leftError);
            bool b = TryConstant(value.Expression.Right, out FP64 right, out AttributeError rightError);
            if (!a || !b)
            {
                return false;
            }

            if (leftError != AttributeError.None || rightError != AttributeError.None)
            {
                error = leftError != AttributeError.None ? leftError : rightError;
                return true;
            }

            switch (value.Expression.Operation)
            {
                case EffectArithmetic.Add:
                    error = AttributeMath.Add(left, right, out result);
                    break;
                case EffectArithmetic.Subtract:
                    error = AttributeMath.Subtract(left, right, out result);
                    break;
                case EffectArithmetic.Multiply:
                    error = AttributeMath.Multiply(left, right, out result);
                    break;
                case EffectArithmetic.Divide:
                    error = AttributeMath.Divide(left, right, out result);
                    break;
                default:
                    error = AttributeError.InvalidOperation;
                    break;
            }

            return true;
        }
    }
}
