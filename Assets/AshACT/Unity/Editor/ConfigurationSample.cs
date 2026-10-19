using System;
using AshACT.Attributes;
using AshACT.Effects;
using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Editor
{
    public static class ConfigurationSample
    {
        [MenuItem("AshACT/创建示例配置")]
        public static void Create()
        {
            string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/AshACT/ConfigurationExample");
            AssetDatabase.CreateFolder("Assets/AshACT", System.IO.Path.GetFileName(folder));
            AttributeSessionSetup setup = CreateAt(folder);
            Selection.activeObject = setup.Catalog;
            EffectGraphWindow.Open(setup.Effects[0], setup.Catalog).SetPreviewContext(setup.Entities[0].Profile);
            AttributeDebugWindow.Open(setup);
        }

        public static AttributeSessionSetup CreateAt(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                throw new ArgumentException("必须提供已存在的资源目录。", nameof(folder));
            }

            AttributeDefinitionAsset stat = ScriptableObject.CreateInstance<AttributeDefinitionAsset>();
            stat.AttributeId = 101;
            stat.DisplayName = "示例数值";
            Save(stat, folder, "Stat");
            AttributeDefinitionAsset resource = ScriptableObject.CreateInstance<AttributeDefinitionAsset>();
            resource.AttributeId = 102;
            resource.DisplayName = "示例资源";
            resource.Kind = AttributeKind.Resource;
            resource.MaximumAttributeId = 101;
            Save(resource, folder, "Resource");
            CharacterAttributeAsset profile = ScriptableObject.CreateInstance<CharacterAttributeAsset>();
            profile.ConfigurationId = 1;
            profile.Attributes = new[]
            {
                new CharacterAttributeEntry { Definition = stat, InitialBase = "100.0000" },
                new CharacterAttributeEntry { Definition = resource, InitialBase = "80.0000" }
            };
            Save(profile, folder, "Character");
            EffectGraphAsset effect = ScriptableObject.CreateInstance<EffectGraphAsset>();
            effect.EffectDefinitionId = 1001;
            effect.DurationKind = EffectDurationKind.Finite;
            effect.DurationFrames = 120;
            effect.MarkerIds = new uint[] { 1 };
            effect.RequiredParameters = new uint[] { 7 };
            effect.Nodes = new[]
            {
                new EffectValueNode { NodeId = 1, Kind = EffectValueKind.SourceCurrent, AttributeId = 101, Position = new Vector2(35, 40) },
                new EffectValueNode { NodeId = 2, Kind = EffectValueKind.SetByCaller, ParameterId = 7, Position = new Vector2(35, 205) },
                new EffectValueNode { NodeId = 3, Kind = EffectValueKind.Arithmetic, Arithmetic = EffectArithmetic.Multiply,
                    LeftNodeId = 1, RightNodeId = 2, Position = new Vector2(350, 100) }
            };
            effect.Modifiers = new[] { new EffectModifierNode { AttributeId = 101, ValueNodeId = 3, Operation = AttributeOperation.Add } };
            Save(effect, folder, "Effect");
            AttributeCatalogAsset catalog = ScriptableObject.CreateInstance<AttributeCatalogAsset>();
            catalog.Attributes = new[] { stat, resource };
            Save(catalog, folder, "Catalog");
            AttributeSessionSetup setup = new AttributeSessionSetup();
            setup.Catalog = catalog;
            setup.Effects = new[] { effect };
            setup.Markers = new[] { new MarkerConfiguration { MarkerId = 1, DisplayName = "示例标记" } };
            setup.Entities = new[]
            {
                new EntityConfiguration { EntityId = 1, Profile = profile },
                new EntityConfiguration { EntityId = 2, Profile = profile }
            };
            return setup;
        }

        private static void Save(UnityEngine.Object asset, string folder, string name)
        {
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset"));
        }
    }
}
