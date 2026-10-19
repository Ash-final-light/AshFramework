using System;
using UnityEngine;

namespace AshACT.Unity
{
    [Serializable]
    public sealed class MarkerDefinitionEntry
    {
        public uint MarkerId;
        public string DisplayName;
        public string Group;
        [TextArea] public string Description;
    }

    /// <summary>共享的标记定义；分组只用于编辑展示，不自动授予角色产生资格。</summary>
    [CreateAssetMenu(menuName = "AshACT/标记定义库")]
    public sealed class MarkerCatalogAsset : VersionedConfigurationAsset
    {
        public MarkerDefinitionEntry[] Markers = Array.Empty<MarkerDefinitionEntry>();
    }
}
