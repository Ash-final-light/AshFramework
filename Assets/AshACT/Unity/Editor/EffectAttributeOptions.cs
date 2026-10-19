using System;
using System.Collections.Generic;

namespace AshACT.Unity.Editor
{
    /// <summary>属性选项只在上下文变更时重建，不把预览角色写入效果。</summary>
    public sealed class EffectAttributeOptions
    {
        private readonly uint[] m_Ids;
        public string[] Labels { get; }
        public IReadOnlyList<uint> Ids { get; }

        public EffectAttributeOptions(AttributeCatalogAsset catalog, CharacterAttributeAsset profile, bool requireProfile)
        {
            Dictionary<uint, AttributeDefinitionAsset> definitions = new Dictionary<uint, AttributeDefinitionAsset>();
            if (catalog != null && catalog.Attributes != null)
            {
                foreach (AttributeDefinitionAsset definition in catalog.Attributes)
                {
                    if (definition != null && definition.AttributeId != 0)
                    {
                        definitions[definition.AttributeId] = definition;
                    }
                }
            }

            List<uint> ids = new List<uint>();
            if (profile != null)
            {
                foreach (CharacterAttributeEntry entry in profile.Attributes ?? Array.Empty<CharacterAttributeEntry>())
                {
                    if (entry?.Definition != null && definitions.TryGetValue(entry.Definition.AttributeId, out AttributeDefinitionAsset definition) &&
                        definition == entry.Definition && !ids.Contains(definition.AttributeId))
                    {
                        ids.Add(definition.AttributeId);
                    }
                }
            }
            else if (!requireProfile)
            {
                ids.AddRange(definitions.Keys);
            }

            ids.Sort();
            m_Ids = ids.ToArray();
            Ids = Array.AsReadOnly(m_Ids);
            Labels = new string[m_Ids.Length + 1];
            Labels[0] = requireProfile && profile == null ? "请先选择施加者预览角色" : "请选择属性（保留当前编号）";
            for (int i = 0; i < m_Ids.Length; i++)
            {
                string name = definitions[m_Ids[i]].DisplayName ?? string.Empty;
                Labels[i + 1] = $"{m_Ids[i]}  {name.Replace('/', '／')}";
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
