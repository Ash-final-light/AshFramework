using System;
using UnityEngine;

namespace AshACT.Unity
{
    [Serializable]
    public sealed class EntityConfiguration
    {
        public ulong EntityId;
        public CharacterAttributeAsset Profile;
    }

    [Serializable]
    public sealed class MarkerConfiguration
    {
        public uint MarkerId;
        public string DisplayName;
    }

    [CreateAssetMenu(menuName = "AshACT/通用属性库")]
    public sealed class AttributeCatalogAsset : VersionedConfigurationAsset
    {
        public AttributeDefinitionAsset[] Attributes = Array.Empty<AttributeDefinitionAsset>();
    }
}
