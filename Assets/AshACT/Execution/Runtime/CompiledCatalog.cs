using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AshACT.Attributes;
using AshACT.Effects;
using Framework.FixedPoint;

namespace AshACT.Unity
{
    public sealed class CompiledProfile
    {
        public uint Id { get; }
        public IReadOnlyList<AttributeConfiguration> Attributes { get; }

        internal CompiledProfile(uint id, AttributeConfiguration[] attributes)
        {
            Id = id;
            Attributes = Array.AsReadOnly((AttributeConfiguration[])attributes.Clone());
        }
    }

    public sealed class CompiledEffect
    {
        public EffectDefinition Definition { get; }
        public IReadOnlyList<uint> RequiredParameters { get; }

        internal CompiledEffect(EffectDefinition definition, uint[] parameters)
        {
            Definition = definition;
            RequiredParameters = Array.AsReadOnly((uint[])parameters.Clone());
        }
    }

    /// <summary>编译成功后一次发布；不保留可变资产或节点引用。</summary>
    public sealed class CompiledAttributeDefinition
    {
        public AttributeId Id { get; }
        public string DisplayName { get; }
        public AttributeKind Kind { get; }
        public FP64 Minimum { get; }
        public FP64 Maximum { get; }
        public AttributeId MaximumAttribute { get; }

        internal CompiledAttributeDefinition(AttributeDefinitionAsset asset, FP64 minimum, FP64 maximum)
        {
            Id = new AttributeId(asset.AttributeId);
            DisplayName = asset.DisplayName;
            Kind = asset.Kind;
            Minimum = minimum;
            Maximum = maximum;
            MaximumAttribute = new AttributeId(asset.MaximumAttributeId);
        }
    }

    public sealed class CompiledAttributeCatalog
    {
        public int SchemaVersion => VersionedConfigurationAsset.CurrentSchemaVersion;
        public int ReleaseVersion { get; }
        public IReadOnlyDictionary<uint, CompiledAttributeDefinition> Attributes { get; }

        internal CompiledAttributeCatalog(int release, Dictionary<uint, CompiledAttributeDefinition> attributes)
        {
            ReleaseVersion = release;
            Attributes = new ReadOnlyDictionary<uint, CompiledAttributeDefinition>(attributes);
        }
    }

    public sealed class CompiledSessionConfiguration
    {
        public CompiledAttributeCatalog Catalog { get; }
        public int SchemaVersion { get; }
        public int ReleaseVersion { get; }
        public IReadOnlyDictionary<uint, CompiledProfile> Profiles { get; }
        public IReadOnlyDictionary<uint, CompiledEffect> Effects { get; }
        public IReadOnlyDictionary<ulong, uint> Entities { get; }

        internal CompiledSessionConfiguration(CompiledAttributeCatalog catalog, Dictionary<uint, CompiledProfile> profiles,
            Dictionary<uint, CompiledEffect> effects, Dictionary<ulong, uint> entities)
        {
            Catalog = catalog;
            SchemaVersion = catalog.SchemaVersion;
            ReleaseVersion = catalog.ReleaseVersion;
            Profiles = new ReadOnlyDictionary<uint, CompiledProfile>(profiles);
            Effects = new ReadOnlyDictionary<uint, CompiledEffect>(effects);
            Entities = new ReadOnlyDictionary<ulong, uint>(entities);
        }
    }
}
