using System;
using System.Collections.Generic;
using System.Linq;
using AshACT.Attributes;
using AshACT.Effects;
using AshACT.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Tests
{
    public sealed class ConfigurationAuthoringTests
    {
        private readonly List<UnityEngine.Object> m_Objects = new List<UnityEngine.Object>();
        private AttributeCatalogAsset m_Catalog;
        private AttributeDefinitionAsset m_Stat;
        private AttributeDefinitionAsset m_Resource;
        private CharacterAttributeAsset m_Profile;
        private EffectGraphAsset m_Effect;

        [SetUp]
        public void SetUp()
        {
            m_Stat = New<AttributeDefinitionAsset>();
            m_Stat.AttributeId = 101;
            m_Resource = New<AttributeDefinitionAsset>();
            m_Resource.AttributeId = 102;
            m_Resource.Kind = AttributeKind.Resource;
            m_Resource.MaximumAttributeId = 101;
            m_Catalog = New<AttributeCatalogAsset>();
            m_Catalog.Attributes = new[] { m_Stat, m_Resource };
            m_Profile = New<CharacterAttributeAsset>();
            m_Profile.ConfigurationId = 1;
            m_Profile.Attributes = new[]
            {
                new CharacterAttributeEntry { Definition = m_Stat, InitialBase = "100" },
                new CharacterAttributeEntry { Definition = m_Resource, InitialBase = "80" }
            };
            m_Effect = New<EffectGraphAsset>();
            m_Effect.EffectDefinitionId = 1001;
            m_Effect.DurationKind = EffectDurationKind.Finite;
            m_Effect.DurationFrames = 180;
            m_Effect.Nodes = new[] { new EffectValueNode { NodeId = 1, Constant = "1.5000" } };
            m_Effect.Modifiers = new[]
            {
                new EffectModifierNode { AttributeId = 101, ValueNodeId = 1, Operation = AttributeOperation.Multiply }
            };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object asset in m_Objects)
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }

            m_Objects.Clear();
        }

        [Test]
        public void AuthoringAssembliesDoNotReferenceExecutionOrDiagnostics()
        {
            foreach (Type type in new[] { typeof(EffectGraphAsset), typeof(ConfigurationValidator), typeof(EffectGraphWindow) })
            {
                string[] references = type.Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
                Assert.That(references.Any(name => name.StartsWith("AshACT.Unity.Execution", StringComparison.Ordinal) ||
                    name.StartsWith("AshACT.Unity.Diagnostics", StringComparison.Ordinal)), Is.False, type.FullName);
            }

            Assert.That(typeof(ConfigurationValidator).Assembly.GetType("AshACT.Unity.AttributeSession"), Is.Null);
            Assert.That(typeof(ConfigurationValidator).Assembly.GetType("AshACT.Unity.ConfigurationCompiler"), Is.Null);
        }

        [Test]
        public void CatalogProfileAndEffectValidateWithoutRuntimeAndDoNotChangeAssets()
        {
            string catalog = EditorJsonUtility.ToJson(m_Catalog);
            string profile = EditorJsonUtility.ToJson(m_Profile);
            string effect = EditorJsonUtility.ToJson(m_Effect);
            Assert.That(ConfigurationValidator.TryValidateCatalog(m_Catalog, out var issues), Is.True, string.Join("\n", issues));
            Assert.That(ConfigurationValidator.TryValidateProfile(m_Catalog, m_Profile, out issues), Is.True, string.Join("\n", issues));
            Assert.That(ConfigurationValidator.TryValidateEffect(m_Catalog, m_Effect, out issues), Is.True, string.Join("\n", issues));
            Assert.That(EditorJsonUtility.ToJson(m_Catalog), Is.EqualTo(catalog));
            Assert.That(EditorJsonUtility.ToJson(m_Profile), Is.EqualTo(profile));
            Assert.That(EditorJsonUtility.ToJson(m_Effect), Is.EqualTo(effect));
        }

        [Test]
        public void ValidMarkerEffectPassesAuthoringValidationWithoutMarkerRuntime()
        {
            MarkerCatalogAsset markers = New<MarkerCatalogAsset>();
            markers.Markers = new[] { new MarkerDefinitionEntry { MarkerId = 7, DisplayName = "燃烧" } };
            m_Effect.MarkerCatalog = markers;
            m_Effect.MarkerOperations = new[] { new EffectMarkerOperation { MarkerId = 7 } };
            Assert.That(ConfigurationValidator.TryValidateEffect(m_Catalog, m_Effect, out var issues),
                Is.True, string.Join("\n", issues));
        }

        [TestCase("node-missing", "InvalidConnection")]
        [TestCase("node-cycle", "InvalidConnection")]
        [TestCase("node-disconnected", "DisconnectedNode")]
        [TestCase("node-duplicate", "InvalidNodeId")]
        [TestCase("parameter-missing", "MissingParameter")]
        [TestCase("parameter-duplicate", "InvalidParameter")]
        [TestCase("attribute-missing", "MissingAttribute")]
        [TestCase("resource-persistent", "ResourcePersistent")]
        [TestCase("duration", "InvalidDefinition")]
        [TestCase("period", "InvalidDefinition")]
        [TestCase("decimal", "InvalidDecimal")]
        [TestCase("kind", "UnsupportedValueSource")]
        [TestCase("operation", "InvalidDefinition")]
        [TestCase("divide", "InvalidOperand")]
        public void InvalidEffectIsLocatedWithoutCreatingRuntimeDefinitions(string mutation, string code)
        {
            switch (mutation)
            {
                case "node-missing":
                    m_Effect.Modifiers[0].ValueNodeId = 9;
                    break;
                case "node-cycle":
                    m_Effect.Nodes[0].Kind = EffectValueKind.Arithmetic;
                    m_Effect.Nodes[0].LeftNodeId = 1;
                    m_Effect.Nodes[0].RightNodeId = 1;
                    break;
                case "node-disconnected":
                    m_Effect.Nodes = new[] { m_Effect.Nodes[0], new EffectValueNode { NodeId = 2 } };
                    break;
                case "node-duplicate":
                    m_Effect.Nodes = new[] { m_Effect.Nodes[0], m_Effect.Nodes[0] };
                    break;
                case "parameter-missing":
                    m_Effect.Nodes[0].Kind = EffectValueKind.SetByCaller;
                    m_Effect.Nodes[0].ParameterId = 3;
                    break;
                case "parameter-duplicate":
                    m_Effect.RequiredParameters = new uint[] { 3, 3 };
                    break;
                case "attribute-missing":
                    m_Effect.Nodes[0].Kind = EffectValueKind.SourceCurrent;
                    m_Effect.Nodes[0].AttributeId = 999;
                    break;
                case "resource-persistent":
                    m_Effect.Modifiers[0].AttributeId = 102;
                    break;
                case "duration":
                    m_Effect.DurationFrames = 0;
                    break;
                case "period":
                    m_Effect.PeriodFrames = -1;
                    break;
                case "decimal":
                    m_Effect.Nodes[0].Constant = "1.00001";
                    break;
                case "kind":
                    m_Effect.Nodes[0].Kind = (EffectValueKind)999;
                    break;
                case "operation":
                    m_Effect.Modifiers[0].Operation = (AttributeOperation)999;
                    break;
                case "divide":
                    m_Effect.Modifiers[0].Operation = AttributeOperation.Divide;
                    m_Effect.Nodes[0].Constant = "0.5";
                    break;
            }

            string before = EditorJsonUtility.ToJson(m_Effect);
            Assert.That(ConfigurationValidator.TryValidateEffect(m_Catalog, m_Effect, out var issues), Is.False);
            Assert.That(issues.Any(issue => issue.Code == code), Is.True, string.Join("\n", issues));
            Assert.That(issues.All(issue => !string.IsNullOrEmpty(issue.Path)), Is.True);
            Assert.That(EditorJsonUtility.ToJson(m_Effect), Is.EqualTo(before));
        }

        [Test]
        public void StaticZeroDivisorIsRejectedEvenWhenNumeratorIsDynamic()
        {
            m_Effect.RequiredParameters = new uint[] { 3 };
            m_Effect.Nodes = new[]
            {
                new EffectValueNode { NodeId = 1, Kind = EffectValueKind.SetByCaller, ParameterId = 3 },
                new EffectValueNode { NodeId = 2, Constant = "0" },
                new EffectValueNode
                {
                    NodeId = 3,
                    Kind = EffectValueKind.Arithmetic,
                    Arithmetic = EffectArithmetic.Divide,
                    LeftNodeId = 1,
                    RightNodeId = 2
                }
            };
            m_Effect.Modifiers[0].ValueNodeId = 3;
            Assert.That(ConfigurationValidator.TryValidateEffect(m_Catalog, m_Effect, out var issues), Is.False);
            Assert.That(issues.Any(issue => issue.Code == "StaticArithmetic" && issue.NodeId == 3), Is.True);
        }

        [Test]
        public void PersistentTargetReadCycleIsRejectedButSourceReadIsValid()
        {
            m_Effect.Nodes[0].Kind = EffectValueKind.TargetCurrent;
            m_Effect.Nodes[0].AttributeId = 101;
            Assert.That(ConfigurationValidator.TryValidateEffect(m_Catalog, m_Effect, out var issues), Is.False);
            Assert.That(issues.Any(issue => issue.Code == "DependencyCycle"), Is.True);
            m_Effect.Nodes[0].Kind = EffectValueKind.SourceCurrent;
            Assert.That(ConfigurationValidator.TryValidateEffect(m_Catalog, m_Effect, out issues), Is.True, string.Join("\n", issues));
        }

        [TestCase("duplicate", "DuplicateAttribute")]
        [TestCase("missing-maximum", "MissingAttribute")]
        [TestCase("range", "InvalidRange")]
        [TestCase("effective-maximum", "InvalidConfiguration")]
        public void ProfileBoundsAndOwnedMaximumAreCheckedWithoutAttributeStore(string mutation, string code)
        {
            if (mutation == "duplicate")
            {
                m_Profile.Attributes = new[] { m_Profile.Attributes[0], m_Profile.Attributes[0] };
            }
            else if (mutation == "missing-maximum")
            {
                m_Profile.Attributes = new[] { m_Profile.Attributes[1] };
            }
            else if (mutation == "range")
            {
                m_Profile.Attributes[0].InitialBase = "1001";
            }
            else
            {
                m_Profile.Attributes[1].OverrideBounds = true;
                m_Profile.Attributes[1].Minimum = "110";
                m_Profile.Attributes[1].InitialBase = "120";
                m_Profile.Attributes[1].MaximumAttributeId = 101;
            }

            Assert.That(ConfigurationValidator.TryValidateProfile(m_Catalog, m_Profile, out var issues), Is.False);
            Assert.That(issues.Any(issue => issue.Code == code), Is.True, string.Join("\n", issues));
        }

        private T New<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            m_Objects.Add(asset);
            return asset;
        }
    }
}
