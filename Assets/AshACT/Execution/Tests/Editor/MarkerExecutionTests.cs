using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AshACT.Unity.Tests
{
    public sealed class MarkerExecutionTests
    {
        [Test]
        public void ValidDraftCannotBeSilentlyCompiledOrReplaceExistingSession()
        {
            MarkerCatalogAsset markers = ScriptableObject.CreateInstance<MarkerCatalogAsset>();
            CharacterAttributeAsset profile = ScriptableObject.CreateInstance<CharacterAttributeAsset>();
            EffectGraphAsset effect = ScriptableObject.CreateInstance<EffectGraphAsset>();
            AttributeCatalogAsset attributes = ScriptableObject.CreateInstance<AttributeCatalogAsset>();
            GameObject owner = new GameObject("标记运行边界测试");
            try
            {
                markers.Markers = new[] { new MarkerDefinitionEntry { MarkerId = 20, DisplayName = "苍炎" } };
                profile.ConfigurationId = 1;
                profile.MarkerCatalog = markers;
                profile.ProducedMarkerIds = new uint[] { 20 };
                effect.EffectDefinitionId = 1;
                effect.MarkerCatalog = markers;
                effect.MarkerOperations = new[] { new EffectMarkerOperation { MarkerId = 20 } };
                Assert.That(ConfigurationValidator.TryValidateEffect(attributes, effect, out IReadOnlyList<ConfigurationIssue> issues),
                    Is.True, string.Join("\n", issues));
                Assert.That(ConfigurationCompiler.TryCompileEffect(attributes, effect, out CompiledEffect compiled, out issues), Is.False);
                Assert.That(compiled, Is.Null);
                Assert.That(issues.Any(issue => issue.Code == "MarkerRuntimeUnavailable"), Is.True);
                AttributeSessionSetup setup = new AttributeSessionSetup
                {
                    Catalog = attributes,
                    Entities = new[] { new EntityConfiguration { EntityId = 1, Profile = profile } }
                };
                AttributeRuntimeHost host = owner.AddComponent<AttributeRuntimeHost>();
                Assert.That(host.TryInitialize(setup, out issues), Is.True, string.Join("\n", issues));
                AttributeSession previous = host.Session;
                setup.Effects = new[] { effect };
                Assert.That(host.TryInitialize(setup, out issues), Is.False);
                Assert.That(host.Session, Is.SameAs(previous));
                Assert.That(issues.Any(issue => issue.Code == "MarkerRuntimeUnavailable"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(attributes);
                UnityEngine.Object.DestroyImmediate(effect);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(markers);
            }
        }
    }
}
