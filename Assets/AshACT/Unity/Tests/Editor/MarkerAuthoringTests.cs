using System;
using System.Collections.Generic;
using System.Linq;
using AshACT.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Tests
{
    public sealed class MarkerAuthoringTests
    {
        private readonly List<UnityEngine.Object> m_Objects = new List<UnityEngine.Object>();
        private MarkerCatalogAsset m_Markers;
        private CharacterAttributeAsset m_Profile;
        private EffectGraphAsset m_Effect;
        private AttributeCatalogAsset m_Attributes;

        [SetUp]
        public void SetUp()
        {
            m_Markers = New<MarkerCatalogAsset>();
            m_Markers.Markers = new[]
            {
                new MarkerDefinitionEntry { MarkerId = 30, DisplayName = "霜寒", Group = "通用" },
                new MarkerDefinitionEntry { MarkerId = 10, DisplayName = "燃烧", Group = "通用" },
                new MarkerDefinitionEntry { MarkerId = 20, DisplayName = "苍炎", Description = "角色专属标记" }
            };
            m_Profile = New<CharacterAttributeAsset>();
            m_Profile.ConfigurationId = 1;
            m_Profile.MarkerCatalog = m_Markers;
            m_Profile.ProducedMarkerIds = new uint[] { 10, 20 };
            m_Effect = New<EffectGraphAsset>();
            m_Effect.EffectDefinitionId = 1;
            m_Effect.MarkerCatalog = m_Markers;
            m_Effect.MarkerOperations = new[] { new EffectMarkerOperation { MarkerId = 20 } };
            m_Attributes = New<AttributeCatalogAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object value in m_Objects)
            {
                if (value != null && !EditorUtility.IsPersistent(value))
                {
                    UnityEngine.Object.DestroyImmediate(value);
                }
            }

            m_Objects.Clear();
        }

        [Test]
        public void AddOptionsRequireExplicitRoleSelectionAndRemovalUsesWholeCatalog()
        {
            EffectMarkerOptions add = new EffectMarkerOptions(m_Markers, m_Profile, true);
            Assert.That(add.At(1), Is.EqualTo(10));
            Assert.That(add.At(2), Is.EqualTo(20));
            Assert.That(add.IndexOf(30), Is.EqualTo(-1));
            Assert.That(add.Labels[2], Does.Contain("苍炎"));
            Assert.That(new EffectMarkerOptions(m_Markers, null, true).Labels.Length, Is.EqualTo(1));
            Assert.That(new EffectMarkerOptions(m_Markers, null, false).IndexOf(30), Is.GreaterThanOrEqualTo(0));
            m_Profile.ProducedMarkerIds = Array.Empty<uint>();
            Assert.That(new EffectMarkerOptions(m_Markers, m_Profile, true).Labels.Length, Is.EqualTo(1));
        }

        [Test]
        public void SameIdsInAnotherCatalogDoNotGrantSourceQualification()
        {
            MarkerCatalogAsset other = New<MarkerCatalogAsset>();
            other.Markers = m_Markers.Markers;
            m_Profile.MarkerCatalog = other;
            Assert.That(new EffectMarkerOptions(m_Markers, m_Profile, true).Labels.Length, Is.EqualTo(1));
            Assert.That(MarkerAuthoringValidation.TryValidateEffect(m_Effect, m_Profile, out var issues), Is.False);
            Assert.That(issues.Any(issue => issue.Code == "MarkerNotProduced"), Is.True);
        }

        [TestCase("zero")]
        [TestCase("duplicate")]
        [TestCase("empty-name")]
        [TestCase("null-entry")]
        public void InvalidDefinitionsAreRejected(string problem)
        {
            if (problem == "zero")
            {
                m_Markers.Markers[0].MarkerId = 0;
            }
            else if (problem == "duplicate")
            {
                m_Markers.Markers[0].MarkerId = 10;
            }
            else if (problem == "empty-name")
            {
                m_Markers.Markers[0].DisplayName = " ";
            }
            else
            {
                m_Markers.Markers[0] = null;
            }

            Assert.That(MarkerAuthoringValidation.TryValidateCatalog(m_Markers, out var issues), Is.False);
            Assert.That(issues.Any(issue => issue.Code == "InvalidMarkerDefinition"), Is.True);
        }

        [TestCase(0u)]
        [TestCase(999u)]
        [TestCase(10u)]
        public void RoleCannotDeclareMissingOrDuplicateIds(uint id)
        {
            m_Profile.ProducedMarkerIds = new uint[] { 10, id };
            Assert.That(MarkerAuthoringValidation.TryValidateProfile(m_Profile, out var issues), Is.False);
            Assert.That(issues.Any(issue => issue.Code == "InvalidProducedMarker"), Is.True);
            Assert.That(ConfigurationCompiler.TryCompileProfile(m_Attributes, m_Profile, out var compiled, out issues), Is.False);
            Assert.That(compiled, Is.Null);
        }

        [Test]
        public void PreviewChangesRetainSavedRequestsAndRemovalDoesNotRequireProducedId()
        {
            m_Profile.ProducedMarkerIds = new uint[] { 10 };
            string before = EditorJsonUtility.ToJson(m_Effect);
            Assert.That(MarkerAuthoringValidation.TryValidateEffect(m_Effect, m_Profile, out var issues), Is.False);
            Assert.That(EditorJsonUtility.ToJson(m_Effect), Is.EqualTo(before));
            m_Effect.MarkerOperations[0].Kind = MarkerOperationKind.Remove;
            m_Effect.MarkerOperations[0].MarkerId = 30;
            Assert.That(MarkerAuthoringValidation.TryValidateEffect(m_Effect, m_Profile, out issues), Is.True);
        }

        [Test]
        public void RenamingDefinitionPreservesIdentityAndUpdatesNames()
        {
            m_Markers.Markers[2].DisplayName = "青焰";
            Assert.That(new EffectMarkerOptions(m_Markers, m_Profile, true).Labels[2], Does.Contain("青焰"));
            Assert.That(m_Profile.ProducedMarkerIds, Is.EqualTo(new uint[] { 10, 20 }));
            Assert.That(m_Effect.MarkerOperations[0].MarkerId, Is.EqualTo(20));
        }

        [TestCase("missing-id", "MissingMarker")]
        [TestCase("zero-count", "InvalidMarkerOperation")]
        [TestCase("period", "MissingMarkerPeriod")]
        [TestCase("enum", "InvalidMarkerOperation")]
        [TestCase("release", "VersionMismatch")]
        public void InvalidOperationsAreLocatedWithoutChangingDraft(string problem, string code)
        {
            EffectMarkerOperation operation = m_Effect.MarkerOperations[0];
            if (problem == "missing-id")
            {
                operation.MarkerId = 999;
            }
            else if (problem == "zero-count")
            {
                operation.Count = 0;
            }
            else if (problem == "period")
            {
                operation.Timing = MarkerOperationTiming.Periodic;
            }
            else if (problem == "release")
            {
                m_Markers.ReleaseVersion = 2;
            }
            else
            {
                operation.Kind = (MarkerOperationKind)99;
            }

            string before = EditorJsonUtility.ToJson(m_Effect);
            Assert.That(MarkerAuthoringValidation.TryValidateEffect(m_Effect, m_Profile, out var issues), Is.False);
            Assert.That(issues.Any(issue => issue.Code == code), Is.True, string.Join("\n", issues));
            Assert.That(EditorJsonUtility.ToJson(m_Effect), Is.EqualTo(before));
        }

        [Test]
        public void ValidDraftCannotBeSilentlyCompiledOrReplaceExistingSession()
        {
            Assert.That(MarkerAuthoringValidation.TryValidateEffect(m_Effect, m_Profile, out var issues), Is.True);
            Assert.That(ConfigurationCompiler.TryCompileEffect(m_Attributes, m_Effect, out var compiled, out issues), Is.False);
            Assert.That(compiled, Is.Null);
            Assert.That(issues.Any(issue => issue.Code == "MarkerRuntimeUnavailable"), Is.True);
            AttributeSessionSetup setup = new AttributeSessionSetup
            {
                Catalog = m_Attributes,
                Entities = new[] { new EntityConfiguration { EntityId = 1, Profile = m_Profile } }
            };
            GameObject owner = new GameObject("标记草稿测试");
            try
            {
                AttributeRuntimeHost host = owner.AddComponent<AttributeRuntimeHost>();
                Assert.That(host.TryInitialize(setup, out issues), Is.True, string.Join("\n", issues));
                AttributeSession previous = host.Session;
                setup.Effects = new[] { m_Effect };
                Assert.That(host.TryInitialize(setup, out issues), Is.False);
                Assert.That(host.Session, Is.SameAs(previous));
                Assert.That(issues.Any(issue => issue.Code == "MarkerRuntimeUnavailable"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void CatalogRoleAndOperationOrderSurviveAssetRoundTripAndUndo()
        {
            string folder = "Assets/AshACT/MarkerAuthoringTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/AshACT", System.IO.Path.GetFileName(folder));
            try
            {
                AssetDatabase.CreateAsset(m_Markers, folder + "/Markers.asset");
                AssetDatabase.CreateAsset(m_Profile, folder + "/Role.asset");
                m_Effect.MarkerOperations = new[]
                {
                    new EffectMarkerOperation { MarkerId = 20 },
                    new EffectMarkerOperation { Kind = MarkerOperationKind.Remove, MarkerId = 30, RemovalMode = MarkerRemovalMode.All }
                };
                AssetDatabase.CreateAsset(m_Effect, folder + "/Effect.asset");
                Undo.RecordObject(m_Profile, "测试可产生标记撤销");
                m_Profile.ProducedMarkerIds = new uint[] { 10 };
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(m_Profile.ProducedMarkerIds, Is.EqualTo(new uint[] { 10, 20 }));
                EditorUtility.SetDirty(m_Profile);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(folder + "/Effect.asset", ImportAssetOptions.ForceUpdate);
                EffectGraphAsset loaded = AssetDatabase.LoadAssetAtPath<EffectGraphAsset>(folder + "/Effect.asset");
                Assert.That(loaded.MarkerCatalog, Is.SameAs(m_Markers));
                Assert.That(loaded.MarkerOperations.Select(item => item.MarkerId), Is.EqualTo(new uint[] { 20, 30 }));
                Assert.That(loaded.MarkerOperations[1].RemovalMode, Is.EqualTo(MarkerRemovalMode.All));
                Assert.That(MarkerAuthoringValidation.TryValidateEffect(loaded, m_Profile, out var issues), Is.True, string.Join("\n", issues));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private T New<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            m_Objects.Add(value);
            return value;
        }
    }
}
