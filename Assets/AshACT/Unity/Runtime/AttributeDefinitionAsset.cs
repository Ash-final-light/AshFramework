using AshACT.Attributes;
using UnityEngine;

namespace AshACT.Unity
{
    [CreateAssetMenu(menuName = "AshACT/属性定义")]
    public sealed class AttributeDefinitionAsset : VersionedConfigurationAsset
    {
        public uint AttributeId;
        public string DisplayName;
        public AttributeKind Kind;
        public string Minimum = "0.0000";
        public string Maximum = "1000.0000";
        public uint MaximumAttributeId;
    }
}
