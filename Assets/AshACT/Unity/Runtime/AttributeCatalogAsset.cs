using System;
using UnityEngine;

namespace AshACT.Unity
{
    [CreateAssetMenu(menuName = "AshACT/通用属性库")]
    public sealed class AttributeCatalogAsset : VersionedConfigurationAsset
    {
        public AttributeDefinitionAsset[] Attributes = Array.Empty<AttributeDefinitionAsset>();
    }
}
