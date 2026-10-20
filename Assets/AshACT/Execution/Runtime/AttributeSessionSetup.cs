using System;

namespace AshACT.Unity
{
    [Serializable]
    public sealed class EntityConfiguration
    {
        public ulong EntityId;
        public CharacterAttributeAsset Profile;
    }

    /// <summary>由运行方临时组装的一次模拟输入，不作为 Unity 资产保存。</summary>
    public sealed class AttributeSessionSetup
    {
        public AttributeCatalogAsset Catalog;
        public EffectGraphAsset[] Effects = Array.Empty<EffectGraphAsset>();
        public EntityConfiguration[] Entities = Array.Empty<EntityConfiguration>();
    }
}
