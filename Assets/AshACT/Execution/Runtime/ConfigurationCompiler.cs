using System;
using System.Collections.Generic;
using AshACT.Attributes;
using AshACT.Effects;
using Framework.FixedPoint;

namespace AshACT.Unity
{
    /// <summary>先执行作者期校验，再生成本次模拟锁定的不可变运行定义。</summary>
    public static class ConfigurationCompiler
    {
        public const int MaximumEntries = ConfigurationValidator.MaximumEntries;
        public const int MaximumNodes = ConfigurationValidator.MaximumNodes;
        public const int MaximumModifiers = ConfigurationValidator.MaximumModifiers;

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

            return TryCompileSession(setup.Catalog, setup.Entities, setup.Effects, out compiled, out issues);
        }

        public static bool TryCompileSession(AttributeCatalogAsset catalog, EntityConfiguration[] entities,
            EffectGraphAsset[] effects, out CompiledSessionConfiguration compiled,
            out IReadOnlyList<ConfigurationIssue> issues)
        {
            Context context = new Context(catalog);
            compiled = context.CompileSession(entities, effects);
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

            internal Context(AttributeCatalogAsset catalog)
            {
                m_Catalog = catalog;
            }

            internal CompiledAttributeCatalog Compile()
            {
                if (!ConfigurationValidator.TryValidateCatalog(m_Catalog, out IReadOnlyList<ConfigurationIssue> issues))
                {
                    Issues.AddRange(issues);
                    return null;
                }

                Dictionary<uint, CompiledAttributeDefinition> definitions = new Dictionary<uint, CompiledAttributeDefinition>();
                foreach (AttributeDefinitionAsset attribute in m_Catalog.Attributes)
                {
                    m_Attributes.Add(attribute.AttributeId, attribute);
                    DecimalValue.TryParse(attribute.Minimum, out FP64 min);
                    DecimalValue.TryParse(attribute.Maximum, out FP64 max);
                    definitions.Add(attribute.AttributeId, new CompiledAttributeDefinition(attribute, min, max));
                }

                return new CompiledAttributeCatalog(m_Catalog.ReleaseVersion, definitions);
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
                EffectGraphAsset[] effects)
            {
                CompiledAttributeCatalog catalog = Compile();
                VersionedConfigurationAsset owner = m_Catalog;
                if (catalog == null || !List(owner, "SessionSetup.Entities", entities) ||
                    !List(owner, "SessionSetup.Effects", effects))
                {
                    return null;
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
                }

                return Issues.Count == 0 ? new CompiledSessionConfiguration(catalog, m_Profiles, m_Effects, m_Entities) : null;
            }

            private void CompileProfile(CharacterAttributeAsset asset)
            {
                if (!ConfigurationValidator.TryValidateProfile(m_Catalog, asset, out IReadOnlyList<ConfigurationIssue> issues))
                {
                    Issues.AddRange(issues);
                    return;
                }

                if (m_ProfileAssets.ContainsKey(asset.ConfigurationId))
                {
                    Error(asset, "ConfigurationId", "InvalidId", "角色配置编号必须唯一。");
                    return;
                }

                m_ProfileAssets.Add(asset.ConfigurationId, asset);
                List<AttributeConfiguration> entries = new List<AttributeConfiguration>();
                foreach (CharacterAttributeEntry entry in asset.Attributes)
                {
                    AttributeDefinitionAsset definition = m_Attributes[entry.Definition.AttributeId];
                    DecimalValue.TryParse(entry.InitialBase, out FP64 initial);
                    DecimalValue.TryParse(entry.OverrideBounds ? entry.Minimum : definition.Minimum, out FP64 min);
                    DecimalValue.TryParse(entry.OverrideBounds ? entry.Maximum : definition.Maximum, out FP64 max);
                    entries.Add(new AttributeConfiguration(new AttributeId(definition.AttributeId), initial, min, max,
                        definition.Kind, new AttributeId(entry.OverrideBounds ? entry.MaximumAttributeId : definition.MaximumAttributeId)));
                }

                entries.Sort((left, right) => left.Id.CompareTo(right.Id));
                m_Profiles.Add(asset.ConfigurationId, new CompiledProfile(asset.ConfigurationId, entries.ToArray()));
            }

            private void CompileEffect(EffectGraphAsset asset)
            {
                if (!ConfigurationValidator.TryValidateEffect(m_Catalog, asset, out IReadOnlyList<ConfigurationIssue> issues))
                {
                    Issues.AddRange(issues);
                    return;
                }

                if (asset.MarkerOperations.Length > 0)
                {
                    Error(asset, "MarkerOperations", "MarkerRuntimeUnavailable", "标记配置有效，但独立标记运行时尚未接入，不能生成可执行效果或会话。");
                    return;
                }

                Dictionary<int, EffectValueNode> nodes = new Dictionary<int, EffectValueNode>();
                foreach (EffectValueNode node in asset.Nodes)
                {
                    nodes.Add(node.NodeId, node);
                }

                List<EffectModifier> modifiers = new List<EffectModifier>();
                foreach (EffectModifierNode modifier in asset.Modifiers)
                {
                    modifiers.Add(new EffectModifier(new AttributeId(modifier.AttributeId), modifier.Operation,
                        Build(modifier.ValueNodeId, nodes), modifier.Priority));
                }

                EffectResult result = EffectDefinition.TryCreate(new EffectDefinitionId(asset.EffectDefinitionId),
                    asset.DurationKind, asset.DurationFrames, asset.PeriodFrames, modifiers, out EffectDefinition definition);
                if (!result.Succeeded)
                {
                    Error(asset, $"Modifiers[{result.ModifierIndex}]/Duration", result.Error.ToString(), "运行定义工厂拒绝该配置。");
                    return;
                }

                m_Effects.Add(asset.EffectDefinitionId, new CompiledEffect(definition, asset.RequiredParameters));
            }

            private static EffectValue Build(int id, Dictionary<int, EffectValueNode> nodes)
            {
                EffectValueNode node = nodes[id];
                if (node.Kind == EffectValueKind.Arithmetic)
                {
                    return EffectValue.Arithmetic(node.Arithmetic, Build(node.LeftNodeId, nodes), Build(node.RightNodeId, nodes));
                }

                FP64 constant = default;
                if (node.Kind == EffectValueKind.Constant)
                {
                    DecimalValue.TryParse(node.Constant, out constant);
                }

                return new EffectValue(node.Kind, constant, node.ParameterId, new AttributeId(node.AttributeId));
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
                    Error(asset, "SchemaVersion/ReleaseVersion", "VersionMismatch", $"要求协议版本 {VersionedConfigurationAsset.CurrentSchemaVersion}，且参与编译的资产发布版本与通用属性库一致。");
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

            private void Error(VersionedConfigurationAsset asset, string path, string code, string message, int nodeId = 0)
            {
                Issues.Add(new ConfigurationIssue(code, asset, path, message, nodeId));
            }
        }

    }
}
