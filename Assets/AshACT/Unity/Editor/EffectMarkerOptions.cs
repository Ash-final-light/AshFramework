using System;
using System.Collections.Generic;

namespace AshACT.Unity.Editor
{
    /// <summary>增加请求按来源角色筛选；移除请求可以选择定义库中的全部标记。</summary>
    public sealed class EffectMarkerOptions
    {
        private readonly uint[] m_Ids;
        public string[] Labels { get; }

        public EffectMarkerOptions(MarkerCatalogAsset catalog, CharacterAttributeAsset source, bool requireProduced)
        {
            SortedDictionary<uint, MarkerDefinitionEntry> definitions = new SortedDictionary<uint, MarkerDefinitionEntry>();
            if (catalog != null && (!requireProduced || source != null && source.MarkerCatalog == catalog))
            {
                foreach (MarkerDefinitionEntry entry in catalog.Markers ?? Array.Empty<MarkerDefinitionEntry>())
                {
                    if (entry != null && entry.MarkerId != 0 && !definitions.ContainsKey(entry.MarkerId) &&
                        (!requireProduced || Array.IndexOf(source.ProducedMarkerIds ?? Array.Empty<uint>(), entry.MarkerId) >= 0))
                    {
                        definitions.Add(entry.MarkerId, entry);
                    }
                }
            }

            m_Ids = new uint[definitions.Count];
            definitions.Keys.CopyTo(m_Ids, 0);
            Labels = new string[m_Ids.Length + 1];
            Labels[0] = requireProduced && source == null ? "请先选择施加者预览角色" : "请选择标记（保留已有编号）";
            for (int i = 0; i < m_Ids.Length; i++)
            {
                MarkerDefinitionEntry entry = definitions[m_Ids[i]];
                string group = string.IsNullOrWhiteSpace(entry.Group) ? string.Empty : $" [{entry.Group}]";
                Labels[i + 1] = $"{entry.DisplayName}{group}  (#{entry.MarkerId})".Replace('/', '／');
            }
        }

        public int IndexOf(uint id)
        {
            return Array.IndexOf(m_Ids, id);
        }

        public uint At(int popupIndex)
        {
            return m_Ids[popupIndex - 1];
        }
    }
}
