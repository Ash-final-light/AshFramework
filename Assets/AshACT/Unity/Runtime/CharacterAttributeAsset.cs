using System;
using UnityEngine;

namespace AshACT.Unity
{
    [Serializable]
    public sealed class CharacterAttributeEntry
    {
        public AttributeDefinitionAsset Definition;
        public string InitialBase = "0.0000";
        public bool OverrideBounds;
        public string Minimum = "0.0000";
        public string Maximum = "1000.0000";
        public uint MaximumAttributeId;
    }

    [CreateAssetMenu(menuName = "AshACT/角色属性配置")]
    public sealed class CharacterAttributeAsset : VersionedConfigurationAsset
    {
        public uint ConfigurationId;
        public CharacterAttributeEntry[] Attributes = Array.Empty<CharacterAttributeEntry>();
        public MarkerCatalogAsset MarkerCatalog;
        public uint[] ProducedMarkerIds = Array.Empty<uint>();
    }
}
