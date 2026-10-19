using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Editor
{
    public static class MarkerAuthoringSample
    {
        [MenuItem("AshACT/创建标记配置示例")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder("Assets/AshACT/ConfigurationExample"))
            {
                AssetDatabase.CreateFolder("Assets/AshACT", "ConfigurationExample");
            }

            MarkerCatalogAsset catalog = ScriptableObject.CreateInstance<MarkerCatalogAsset>();
            catalog.Markers = new[]
            {
                new MarkerDefinitionEntry { MarkerId = 1, DisplayName = "燃烧", Group = "通用", Description = "共享标记定义；角色仍需显式声明产生资格。" },
                new MarkerDefinitionEntry { MarkerId = 2, DisplayName = "苍炎", Group = "苍炎术士", Description = "由苍炎术士产生，可请求挂到其他实体身上。" },
                new MarkerDefinitionEntry { MarkerId = 3, DisplayName = "霜寒", Group = "通用", Description = "移除请求可选择此标记，无需角色具有产生资格。" }
            };
            Save(catalog, "MarkerCatalog");
            CharacterAttributeAsset profile = ScriptableObject.CreateInstance<CharacterAttributeAsset>();
            profile.ConfigurationId = 9001;
            profile.MarkerCatalog = catalog;
            profile.ProducedMarkerIds = new uint[] { 1, 2 };
            Save(profile, "MarkerCharacter");
            EffectGraphAsset effect = ScriptableObject.CreateInstance<EffectGraphAsset>();
            effect.EffectDefinitionId = 9001;
            effect.MarkerCatalog = catalog;
            effect.MarkerOperations = new[]
            {
                new EffectMarkerOperation { MarkerId = 2 },
                new EffectMarkerOperation { Kind = MarkerOperationKind.Remove, MarkerId = 3, RemovalMode = MarkerRemovalMode.All }
            };
            Save(effect, "MarkerEffect");
            AssetDatabase.SaveAssetIfDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(profile);
            AssetDatabase.SaveAssetIfDirty(effect);
            Selection.activeObject = profile;
            EffectGraphWindow window = EffectGraphWindow.Open(effect);
            window.SetPreviewContext(profile);
        }

        private static void Save(Object asset, string name)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath($"Assets/AshACT/ConfigurationExample/{name}.asset");
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
