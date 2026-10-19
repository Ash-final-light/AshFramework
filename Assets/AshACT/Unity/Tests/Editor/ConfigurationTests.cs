using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using AshACT.Attributes;
using AshACT.Effects;
using AshACT.Unity.Editor;
using Framework.FixedPoint;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.TestTools;

namespace AshACT.Unity.Tests
{
    public sealed class ConfigurationTests
    {
        private readonly List<UnityEngine.Object> m_Objects = new List<UnityEngine.Object>();
        private AttributeCatalogAsset m_Catalog;
        private AttributeSessionSetup m_Setup;
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
            m_Profile = New<CharacterAttributeAsset>();
            m_Profile.ConfigurationId = 1;
            m_Profile.Attributes = new[]
            {
                new CharacterAttributeEntry { Definition = m_Stat, InitialBase = "100.0000" },
                new CharacterAttributeEntry { Definition = m_Resource, InitialBase = "80.0000" }
            };
            m_Effect = New<EffectGraphAsset>();
            m_Effect.EffectDefinitionId = 1001;
            m_Effect.DurationKind = EffectDurationKind.Finite;
            m_Effect.DurationFrames = 3;
            m_Effect.Nodes = new[] { new EffectValueNode { NodeId = 1, Constant = "1.5000" } };
            m_Effect.Modifiers = new[]
            {
                new EffectModifierNode { AttributeId = 101, ValueNodeId = 1, Operation = AttributeOperation.Multiply }
            };
            m_Catalog = New<AttributeCatalogAsset>();
            m_Catalog.Attributes = new[] { m_Stat, m_Resource };
            m_Setup = new AttributeSessionSetup();
            m_Setup.Catalog = m_Catalog;
            m_Setup.Effects = new[] { m_Effect };
            m_Setup.Entities = new[]
            {
                new EntityConfiguration { EntityId = 1, Profile = m_Profile },
                new EntityConfiguration { EntityId = 2, Profile = m_Profile }
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

        [TestCase("0.0001", 429497L)]
        [TestCase("-0.0001", -429497L)]
        [TestCase("1000000000.0000", 4294967296000000000L)]
        [TestCase("1.5000", 6442450944L)]
        public void DecimalInputPreservesExactRaw(string text, long raw)
        {
            Assert.That(DecimalValue.TryParse(text, out FP64 value), Is.True);
            Assert.That(value.Raw, Is.EqualTo(raw));
            Assert.That(DecimalValue.Format(value), Is.EqualTo(text));
        }

        [TestCase("1.00001")]
        [TestCase("1e2")]
        [TestCase("1,5")]
        [TestCase(" 1")]
        [TestCase("1000000000.0001")]
        [TestCase(null)]
        public void DecimalInputRejectsInvalidText(string text)
        {
            Assert.That(DecimalValue.TryParse(text, out FP64 value), Is.False);
        }

        [TestCase(134217728L, "0.0312")]
        [TestCase(402653184L, "0.0938")]
        [TestCase(-134217728L, "-0.0312")]
        public void DisplayUsesMidpointToEven(long raw, string expected)
        {
            Assert.That(DecimalValue.Format(FP64.FromRaw(raw)), Is.EqualTo(expected));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void PreviousSchemaEffectsAreRejectedInsteadOfSilentlyDroppingTheirPayload(int schema)
        {
            m_Effect.SchemaVersion = schema;
            Assert.That(ConfigurationCompiler.TryCompileSession(m_Setup, out CompiledSessionConfiguration compiled,
                out IReadOnlyList<ConfigurationIssue> issues), Is.False);
            Assert.That(compiled, Is.Null);
            Assert.That(issues.Any(issue => issue.Code == "VersionMismatch"), Is.True);
        }

        [TestCase("attribute-zero", "InvalidId")]
        [TestCase("attribute-duplicate", "InvalidId")]
        [TestCase("profile-zero", "InvalidId")]
        [TestCase("profile-duplicate", "InvalidId")]
        [TestCase("effect-duplicate", "InvalidId")]
        [TestCase("entity-zero", "InvalidId")]
        [TestCase("entity-duplicate", "InvalidId")]
        [TestCase("schema", "VersionMismatch")]
        [TestCase("release", "VersionMismatch")]
        [TestCase("node-duplicate", "InvalidNodeId")]
        [TestCase("node-missing", "InvalidConnection")]
        [TestCase("node-disconnected", "DisconnectedNode")]
        [TestCase("decimal", "InvalidDecimal")]
        [TestCase("initial", "InvalidRange")]
        [TestCase("bounds", "InvalidRange")]
        [TestCase("maximum", "MissingAttribute")]
        [TestCase("read", "MissingAttribute")]
        [TestCase("parameter", "MissingParameter")]
        [TestCase("resource", "ResourcePersistent")]
        [TestCase("limit", "CollectionLimit")]
        [TestCase("null-list", "CollectionLimit")]
        public void InvalidSessionNeverPublishesPartialConfiguration(string mutation, string code)
        {
            switch (mutation)
            {
                case "attribute-zero":
                    m_Stat.AttributeId = 0;
                    break;
                case "attribute-duplicate":
                    m_Catalog.Attributes = new[] { m_Stat, m_Stat };
                    break;
                case "profile-zero":
                    m_Profile.ConfigurationId = 0;
                    break;
                case "profile-duplicate":
                    CharacterAttributeAsset duplicate = New<CharacterAttributeAsset>();
                    duplicate.ConfigurationId = m_Profile.ConfigurationId;
                    duplicate.Attributes = m_Profile.Attributes;
                    m_Setup.Entities[1].Profile = duplicate;
                    break;
                case "effect-duplicate":
                    m_Setup.Effects = new[] { m_Effect, m_Effect };
                    break;
                case "entity-zero":
                    m_Setup.Entities[0].EntityId = 0;
                    break;
                case "entity-duplicate":
                    m_Setup.Entities[1].EntityId = 1;
                    break;
                case "schema":
                    m_Effect.SchemaVersion = VersionedConfigurationAsset.CurrentSchemaVersion + 1;
                    break;
                case "release":
                    m_Profile.ReleaseVersion = 2;
                    break;
                case "node-duplicate":
                    m_Effect.Nodes = new[] { m_Effect.Nodes[0], m_Effect.Nodes[0] };
                    break;
                case "node-missing":
                    m_Effect.Modifiers[0].ValueNodeId = 2;
                    break;
                case "node-disconnected":
                    m_Effect.Nodes = new[] { m_Effect.Nodes[0], new EffectValueNode { NodeId = 2 } };
                    break;
                case "decimal":
                    m_Effect.Nodes[0].Constant = "0.00001";
                    break;
                case "initial":
                    m_Profile.Attributes[0].InitialBase = "1001";
                    break;
                case "bounds":
                    m_Stat.Minimum = "1001";
                    break;
                case "maximum":
                    m_Resource.MaximumAttributeId = 999;
                    break;
                case "read":
                    m_Effect.Nodes[0].Kind = EffectValueKind.SourceBase;
                    m_Effect.Nodes[0].AttributeId = 999;
                    break;
                case "parameter":
                    m_Effect.Nodes[0].Kind = EffectValueKind.SetByCaller;
                    m_Effect.Nodes[0].ParameterId = 7;
                    break;
                case "resource":
                    m_Effect.Modifiers[0].AttributeId = 102;
                    break;
                case "limit":
                    m_Effect.Nodes = new EffectValueNode[ConfigurationCompiler.MaximumNodes + 1];
                    break;
                case "null-list":
                    m_Setup.Entities = null;
                    break;
            }

            Assert.That(ConfigurationCompiler.TryCompileSession(m_Setup, out CompiledSessionConfiguration compiled,
                out IReadOnlyList<ConfigurationIssue> issues), Is.False);
            Assert.That(compiled, Is.Null);
            Assert.That(issues.Any(issue => issue.Code == code), Is.True, string.Join("\n", issues));
            Assert.That(issues.All(issue => !string.IsNullOrEmpty(issue.Path)), Is.True);
        }

        [Test]
        public void CommonCatalogCompilesIndependentlyFromCharactersAndSessions()
        {
            m_Profile.ConfigurationId = 0;
            m_Effect.SchemaVersion = 99;
            m_Setup.Entities = null;
            Assert.That(ConfigurationCompiler.TryCompile(m_Catalog, out CompiledAttributeCatalog compiled,
                out IReadOnlyList<ConfigurationIssue> issues), Is.True, string.Join("\n", issues));
            Assert.That(compiled.Attributes.Keys, Is.EquivalentTo(new uint[] { 101, 102 }));
            m_Stat.DisplayName = "已修改";
            m_Stat.Maximum = "10";
            Assert.That(compiled.Attributes[101].DisplayName, Is.Not.EqualTo(m_Stat.DisplayName));
            Assert.That(compiled.Attributes[101].Maximum.Raw, Is.EqualTo(FP64.FromInt(1000).Raw));
            Assert.That(ConfigurationCompiler.TryCompileSession(m_Setup, out CompiledSessionConfiguration session,
                out issues), Is.False);
            Assert.That(session, Is.Null);
        }

        [Test]
        public void CharacterAndEffectCompileWithoutAnyEntityRegistration()
        {
            Assert.That(ConfigurationCompiler.TryCompileProfile(m_Catalog, m_Profile, out CompiledProfile profile,
                out IReadOnlyList<ConfigurationIssue> issues), Is.True, string.Join("\n", issues));
            Assert.That(profile.Attributes.Count, Is.EqualTo(2));
            Assert.That(ConfigurationCompiler.TryCompileEffect(m_Catalog, m_Effect, out CompiledEffect effect,
                out issues), Is.True, string.Join("\n", issues));
            Assert.That(effect.Definition.Id.Value, Is.EqualTo(1001));
            Assert.That(ConfigurationCompiler.TryCompileSession(m_Catalog, m_Setup.Entities, m_Setup.Effects,
                out CompiledSessionConfiguration compiled, out issues), Is.True, string.Join("\n", issues));
            Assert.That(compiled.Entities.Count, Is.EqualTo(2));
            Assert.That(typeof(EffectGraphAsset).GetField("SourceProfile"), Is.Null);
            Assert.That(typeof(EffectGraphAsset).GetField("TargetProfile"), Is.Null);
        }

        [Test]
        public void EditorAttributeOptionsUseActualCharacterSelectionAndKeepStableIds()
        {
            m_Profile.Attributes = new[] { m_Profile.Attributes[0] };
            EffectAttributeOptions source = new EffectAttributeOptions(m_Catalog, m_Profile, true);
            Assert.That(source.Ids, Is.EqualTo(new uint[] { 101 }));
            Assert.That(source.IndexOf(102), Is.EqualTo(-1));
            Assert.That(new EffectAttributeOptions(m_Catalog, null, true).Ids, Is.Empty);
            Assert.That(new EffectAttributeOptions(m_Catalog, null, false).Ids, Is.EqualTo(new uint[] { 101, 102 }));
            Assert.That(new EffectAttributeOptions(m_Catalog, m_Profile, false).Ids, Is.EqualTo(new uint[] { 101 }));
            m_Effect.Nodes[0].AttributeId = 102;
            EffectAttributeOptions changed = new EffectAttributeOptions(m_Catalog, m_Profile, true);
            Assert.That(changed.IndexOf(m_Effect.Nodes[0].AttributeId), Is.EqualTo(-1));
            Assert.That(m_Effect.Nodes[0].AttributeId, Is.EqualTo(102));
            AttributeDefinitionAsset external = New<AttributeDefinitionAsset>();
            external.AttributeId = 101;
            m_Profile.Attributes[0].Definition = external;
            Assert.That(new EffectAttributeOptions(m_Catalog, m_Profile, true).Ids, Is.Empty);
        }

        [TestCase(EffectValueKind.SourceBase)]
        [TestCase(EffectValueKind.SourceCurrent)]
        public void SourceReadsUseEachActualSourceAndMissingOwnedAttributeIsAtomic(EffectValueKind kind)
        {
            CharacterAttributeAsset other = New<CharacterAttributeAsset>();
            other.ConfigurationId = 2;
            other.Attributes = new[] { new CharacterAttributeEntry { Definition = m_Stat, InitialBase = "20" } };
            m_Setup.Entities = new[]
            {
                new EntityConfiguration { EntityId = 1, Profile = m_Profile },
                new EntityConfiguration { EntityId = 2, Profile = other },
                new EntityConfiguration { EntityId = 3, Profile = m_Profile }
            };
            m_Effect.Nodes[0].Kind = kind;
            m_Effect.Nodes[0].AttributeId = 102;
            m_Effect.Modifiers[0].Operation = AttributeOperation.Add;
            AttributeSession session = new AttributeSession(Compile());
            session.BeginFrame(0);
            string before = session.Runtime.CaptureState().Hash;
            Assert.That(session.TryApply(1001, 2, 3, Array.Empty<EffectParameter>(), out string error), Is.False);
            Assert.That(session.LastResult.AttributeId.Value, Is.EqualTo(102));
            Assert.That(session.Runtime.CaptureState().Hash, Is.EqualTo(before));
            Assert.That(session.TryApply(1001, 1, 3, Array.Empty<EffectParameter>(), out error), Is.True, error);
            Assert.That(Read(session, 3).Raw, Is.EqualTo(FP64.FromInt(180).Raw));
        }

        [TestCase(EffectValueKind.SourceBase)]
        [TestCase(EffectValueKind.SourceCurrent)]
        public void OneReusableEffectAcceptsDifferentSourceProfiles(EffectValueKind kind)
        {
            CharacterAttributeAsset other = New<CharacterAttributeAsset>();
            other.ConfigurationId = 2;
            other.Attributes = new[] { new CharacterAttributeEntry { Definition = m_Stat, InitialBase = "20" } };
            m_Setup.Entities[1].Profile = other;
            m_Effect.DurationKind = EffectDurationKind.Instant;
            m_Effect.DurationFrames = 0;
            m_Effect.Nodes[0].Kind = kind;
            m_Effect.Nodes[0].AttributeId = 101;
            m_Effect.Modifiers[0].Operation = AttributeOperation.Add;
            AttributeSession session = new AttributeSession(Compile());
            session.BeginFrame(0);
            Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.True, error);
            Assert.That(Read(session, 2).Raw, Is.EqualTo(FP64.FromInt(120).Raw));
            Assert.That(session.TryApply(1001, 2, 1, Array.Empty<EffectParameter>(), out error), Is.True, error);
            Assert.That(Read(session, 1).Raw, Is.EqualTo(FP64.FromInt(220).Raw));
        }

        [Test]
        public void ExpressionCycleAndStaticDependencyCycleAreRejected()
        {
            m_Effect.Nodes[0].Kind = EffectValueKind.Arithmetic;
            m_Effect.Nodes[0].LeftNodeId = 1;
            m_Effect.Nodes[0].RightNodeId = 1;
            Reject("InvalidConnection");
            m_Effect.Nodes[0].Kind = EffectValueKind.TargetCurrent;
            m_Effect.Nodes[0].LeftNodeId = 0;
            m_Effect.Nodes[0].RightNodeId = 0;
            m_Effect.Nodes[0].AttributeId = 101;
            Reject("DependencyCycle");
            m_Effect.Nodes[0].AttributeId = 102;
            AttributeSession session = new AttributeSession(Compile());
            session.BeginFrame(0);
            Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.False);
            Assert.That(session.LastResult.Error, Is.EqualTo(EffectError.DependencyCycle));
        }

        [TestCase(EffectArithmetic.Divide, "0", "InvalidOperand")]
        [TestCase(EffectArithmetic.Multiply, "1000000000", "InvalidOperand")]
        public void StaticArithmeticErrorsAreLocated(EffectArithmetic arithmetic, string right, string code)
        {
            m_Effect.Nodes = new[]
            {
                new EffectValueNode { NodeId = 1, Constant = "1000000000" },
                new EffectValueNode { NodeId = 2, Constant = right },
                new EffectValueNode { NodeId = 3, Kind = EffectValueKind.Arithmetic, Arithmetic = arithmetic, LeftNodeId = 1, RightNodeId = 2 }
            };
            m_Effect.Modifiers[0].ValueNodeId = 3;
            ConfigurationIssue issue = Reject(code);
            Assert.That(issue.NodeId, Is.EqualTo(3));
            Assert.That(issue.Asset, Is.SameAs(m_Effect));
        }

        [Test]
        public void CharactersHaveIndependentSelectionsBoundsAndStores()
        {
            CharacterAttributeAsset other = New<CharacterAttributeAsset>();
            other.ConfigurationId = 2;
            other.Attributes = new[]
            {
                new CharacterAttributeEntry { Definition = m_Stat, InitialBase = "20", OverrideBounds = true, Minimum = "10", Maximum = "30" }
            };
            m_Setup.Entities[1].Profile = other;
            AttributeSession session = new AttributeSession(Compile());
            Assert.That(session.BeginFrame(0).Succeeded, Is.True);
            Assert.That(session.Execute(1, new[] { new AttributeCommand(AttributeCommandKind.Instant, new AttributeId(101), AttributeOperation.Add, FP64.One) }).Succeeded, Is.True);
            session.Runtime.TryGetController(new EntityId(2), out EffectController second);
            Assert.That(second.Attributes.TryGetValue(new AttributeId(102), out AttributeValue absent), Is.False);
            Assert.That(second.Attributes.TryGetValue(new AttributeId(101), out AttributeValue value), Is.True);
            Assert.That(value.BaseValue.Raw, Is.EqualTo(FP64.FromInt(20).Raw));
            Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.True, error);
            Assert.That(Read(session, 2).Raw, Is.EqualTo(FP64.FromInt(30).Raw));
        }

        [Test]
        public void TargetReadsResolveTheProfileOfEachRuntimeTarget()
        {
            CharacterAttributeAsset other = New<CharacterAttributeAsset>();
            other.ConfigurationId = 2;
            other.Attributes = new[] { new CharacterAttributeEntry { Definition = m_Stat, InitialBase = "20" } };
            m_Setup.Entities[1].Profile = other;
            m_Effect.DurationKind = EffectDurationKind.Instant;
            m_Effect.DurationFrames = 0;
            m_Effect.Nodes[0].Kind = EffectValueKind.TargetBase;
            m_Effect.Nodes[0].AttributeId = 101;
            m_Effect.Modifiers[0].Operation = AttributeOperation.Add;
            AttributeSession session = new AttributeSession(Compile());
            session.BeginFrame(0);
            Assert.That(session.TryApply(1001, 1, 1, Array.Empty<EffectParameter>(), out string error), Is.True, error);
            Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out error), Is.True, error);
            Assert.That(Read(session, 1).Raw, Is.EqualTo(FP64.FromInt(200).Raw));
            Assert.That(Read(session, 2).Raw, Is.EqualTo(FP64.FromInt(40).Raw));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingRuntimeTargetAttributeRejectsWholeEffect(bool reading)
        {
            CharacterAttributeAsset other = New<CharacterAttributeAsset>();
            other.ConfigurationId = 2;
            other.Attributes = new[] { new CharacterAttributeEntry { Definition = m_Stat, InitialBase = "20" } };
            m_Setup.Entities[1].Profile = other;
            if (reading)
            {
                m_Effect.Nodes[0].Kind = EffectValueKind.TargetBase;
                m_Effect.Nodes[0].AttributeId = 102;
            }
            else
            {
                m_Effect.DurationKind = EffectDurationKind.Finite;
                m_Effect.PeriodFrames = 1;
                m_Effect.Modifiers[0].AttributeId = 102;
            }

            AttributeSession session = new AttributeSession(Compile());
            session.BeginFrame(0);
            string hash = session.Runtime.CaptureState().Hash;
            Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.False);
            Assert.That(session.LastResult.AttributeId.Value, Is.EqualTo(102));
            Assert.That(session.Runtime.CaptureState().Hash, Is.EqualTo(hash));
        }

        [Test]
        public void RoleSpecificResourceCapCycleIsCheckedForTheChosenTarget()
        {
            CharacterAttributeAsset other = New<CharacterAttributeAsset>();
            other.ConfigurationId = 2;
            other.Attributes = new[]
            {
                new CharacterAttributeEntry { Definition = m_Stat, InitialBase = "100" },
                new CharacterAttributeEntry { Definition = m_Resource, InitialBase = "20", OverrideBounds = true, Maximum = "1000" }
            };
            m_Setup.Entities[1].Profile = other;
            m_Effect.Nodes[0].Kind = EffectValueKind.TargetCurrent;
            m_Effect.Nodes[0].AttributeId = 102;
            m_Effect.Modifiers[0].Operation = AttributeOperation.Add;
            AttributeSession session = new AttributeSession(Compile());
            session.BeginFrame(0);
            Assert.That(session.TryApply(1001, 1, 1, Array.Empty<EffectParameter>(), out string error), Is.False);
            Assert.That(session.LastResult.Error, Is.EqualTo(EffectError.DependencyCycle));
            Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out error), Is.True, error);
            Assert.That(Read(session, 2).Raw, Is.EqualTo(FP64.FromInt(120).Raw));
        }

        [UnityTest]
        public IEnumerator DebugRepaintsReuseSnapshotAndOnlyDrawVisibleRows()
        {
            m_Setup.Entities = Enumerable.Range(1, 128).Select(id => new EntityConfiguration
            {
                EntityId = (ulong)id,
                Profile = m_Profile
            }).ToArray();
            AttributeDebugWindow window = ScriptableObject.CreateInstance<AttributeDebugWindow>();
            try
            {
                window.position = new Rect(50, 50, 1100, 700);
                window.Show();
                Assert.That(window.InitializePreview(m_Setup), Is.True);
                window.RefreshState();
                int captures = window.SnapshotRefreshCount;
                DebugStatePresentation before = window.Presentation;
                window.Session.BeginFrame(0);
                for (int i = 0; i < 10; i++)
                {
                    window.Repaint();
                    yield return null;
                }

                Assert.That(window.SnapshotRefreshCount, Is.EqualTo(captures));
                Assert.That(window.Presentation, Is.SameAs(before));
                Assert.That(window.LastDrawnRowCount, Is.GreaterThan(0).And.LessThan(80));
                Assert.That(window.Presentation.Lines.Count, Is.GreaterThan(1000));
                window.RefreshState();
                Assert.That(window.Presentation.Hash, Is.Not.EqualTo(before.Hash));
                Assert.That(window.SnapshotRefreshCount, Is.EqualTo(captures + 1));
            }
            finally
            {
                window.Close();
            }
        }

        [UnityTest]
        public IEnumerator NativePortsPersistConnectionsAndReuseGraphAcrossRepaints()
        {
            EffectGraphWindow window = EffectGraphWindow.Open(m_Effect, m_Catalog);
            try
            {
                yield return WaitForGraph(window, m_Effect, -1);
                yield return null;
                yield return null;
                EffectGraphView graph = window.Graph;
                Assert.That(graph, Is.Not.Null);
                Assert.That(graph.edges.ToList().Count, Is.EqualTo(1));
                Assert.That(graph.ports.ToList().All(port => port.edgeConnector != null), Is.True);
                int builds = graph.BuildCount;
                for (int i = 0; i < 5; i++)
                {
                    window.Repaint();
                    yield return null;
                }

                Assert.That(graph.BuildCount, Is.EqualTo(builds));
                window.AddNode(EffectValueKind.Constant);
                yield return WaitForGraph(window, m_Effect, builds);
                Port output = graph.ports.ToList().Single(port => port.direction == Direction.Output && port.node.title.StartsWith("#2 "));
                Port input = graph.ports.ToList().Single(port => port.direction == Direction.Input);
                Assert.That(input.capacity, Is.EqualTo(Port.Capacity.Single));
                Assert.That(output.capacity, Is.EqualTo(Port.Capacity.Multi));
                Assert.That(graph.GetCompatiblePorts(output, null), Does.Contain(input));
                builds = graph.BuildCount;
                graph.graphViewChanged(new GraphViewChange { edgesToCreate = new List<Edge> { output.ConnectTo(input) } });
                yield return WaitForGraph(window, m_Effect, builds);
                Assert.That(m_Effect.Modifiers[0].ValueNodeId, Is.EqualTo(2));
                Assert.That(graph.edges.ToList().Count, Is.EqualTo(1));
                builds = graph.BuildCount;
                graph.DeleteElements(graph.edges.ToList());
                yield return WaitForGraph(window, m_Effect, builds);
                Assert.That(m_Effect.Modifiers[0].ValueNodeId, Is.EqualTo(0));
                Assert.That(graph.edges.ToList(), Is.Empty);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void SessionForwardsSubscriptionsAndRestoreResetWithoutSyntheticChanges()
        {
            AttributeSession session = new AttributeSession(Compile());
            EffectRuntimeState saved = session.Runtime.CaptureState();
            List<AttributeChange> changes = new List<AttributeChange>();
            int resets = 0;
            using (session.Subscribe(2, 101, changes.Add))
            {
                using (session.SubscribeStateReset(reset => resets++))
                {
                    Assert.That(session.BeginFrame(0).Succeeded, Is.True);
                    Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.True, error);
                    Assert.That(changes.Single().Cause.Reason, Is.EqualTo(AttributeChangeReason.EffectApplied));
                    Assert.That(session.Runtime.TryRestoreState(saved, Array.Empty<EffectDefinition>()).Succeeded, Is.True);
                    Assert.That(changes.Count, Is.EqualTo(1));
                    Assert.That(resets, Is.EqualTo(1));
                }
            }

            Assert.That(session.BeginFrame(0).Succeeded, Is.True);
            Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out _), Is.True);
            Assert.That(changes.Count, Is.EqualTo(1));
        }

        [Test]
        public void HostSessionChangedSupportsExplicitUnsubscribeAndRebindOnlyAfterSuccess()
        {
            GameObject gameObject = new GameObject("属性会话订阅测试");
            IDisposable token = null;
            try
            {
                AttributeRuntimeHost host = gameObject.AddComponent<AttributeRuntimeHost>();
                List<AttributeSession> previousSessions = new List<AttributeSession>();
                List<AttributeChange> changes = new List<AttributeChange>();
                host.SessionChanged += (previous, current) =>
                {
                    Assert.That(host.Session, Is.SameAs(current));
                    previousSessions.Add(previous);
                    token?.Dispose();
                    token = current.Subscribe(2, 101, changes.Add);
                };
                Assert.That(host.TryInitialize(m_Setup, out _), Is.True);
                AttributeSession first = host.Session;
                Assert.That(previousSessions.Single(), Is.Null);
                Assert.That(first.BeginFrame(0).Succeeded, Is.True);
                Assert.That(first.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out _), Is.True);
                Assert.That(host.TryInitialize(null, out _), Is.False);
                Assert.That(previousSessions.Count, Is.EqualTo(1));
                Assert.That(host.Session, Is.SameAs(first));
                Assert.That(host.TryInitialize(m_Setup, out _), Is.True);
                Assert.That(previousSessions[1], Is.SameAs(first));
                Assert.That(first.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out _), Is.True);
                Assert.That(changes.Count, Is.EqualTo(1));
                Assert.That(host.Session.BeginFrame(0).Succeeded, Is.True);
                Assert.That(host.Session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out _), Is.True);
                Assert.That(changes.Count, Is.EqualTo(2));
            }
            finally
            {
                token?.Dispose();
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void HostRejectsInitializationDuringAttributeAndSessionChangeCallbacks()
        {
            GameObject gameObject = new GameObject("属性通知重入测试");
            try
            {
                AttributeRuntimeHost host = gameObject.AddComponent<AttributeRuntimeHost>();
                List<string> rejections = new List<string>();
                host.SessionChanged += (previous, current) =>
                {
                    Assert.That(host.TryInitialize(m_Setup, out IReadOnlyList<ConfigurationIssue> issues), Is.False);
                    rejections.Add(issues.Single().Code);
                };
                Assert.That(host.TryInitialize(m_Setup, out _), Is.True);
                AttributeSession first = host.Session;
                using (first.Subscribe(2, 101, change =>
                {
                    Assert.That(host.TryInitialize(m_Setup, out IReadOnlyList<ConfigurationIssue> issues), Is.False);
                    rejections.Add(issues.Single().Code);
                }))
                {
                    Assert.That(first.BeginFrame(0).Succeeded, Is.True);
                    Assert.That(first.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out _), Is.True);
                }

                Assert.That(host.Session, Is.SameAs(first));
                Assert.That(rejections, Is.EqualTo(new[] { "NotificationReentrancy", "NotificationReentrancy" }));
                Assert.That(first.Runtime.LastNotificationException, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void HostSessionChangedExceptionsDoNotInvalidateInitializationOrSkipOtherListeners()
        {
            GameObject gameObject = new GameObject("属性会话异常测试");
            try
            {
                AttributeRuntimeHost host = gameObject.AddComponent<AttributeRuntimeHost>();
                int delivered = 0;
                host.SessionChanged += (previous, current) =>
                {
                    throw new InvalidOperationException("会话回调异常");
                };
                host.SessionChanged += (previous, current) => delivered++;
                LogAssert.Expect(LogType.Exception, "InvalidOperationException: 会话回调异常");
                Assert.That(host.TryInitialize(m_Setup, out _), Is.True);
                Assert.That(host.Session, Is.Not.Null);
                Assert.That(delivered, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static IEnumerator WaitForGraph(EffectGraphWindow window, EffectGraphAsset asset, int afterBuild)
        {
            double deadline = EditorApplication.timeSinceStartup + 3;
            while (EditorApplication.timeSinceStartup < deadline &&
                (window.Graph == null || window.Graph.Asset != asset || window.Graph.BuildCount <= afterBuild))
            {
                yield return null;
            }

            Assert.That(window.Graph, Is.Not.Null);
            Assert.That(window.Graph.Asset, Is.SameAs(asset));
            Assert.That(window.Graph.BuildCount, Is.GreaterThan(afterBuild));
        }

        [TestCase(EffectValueKind.SourceBase, EffectArithmetic.Add, 102)]
        [TestCase(EffectValueKind.SourceCurrent, EffectArithmetic.Subtract, 98)]
        [TestCase(EffectValueKind.TargetBase, EffectArithmetic.Multiply, 200)]
        [TestCase(EffectValueKind.TargetCurrent, EffectArithmetic.Divide, 50)]
        public void AllReadChannelsAndArithmeticCompileAndExecute(EffectValueKind kind, EffectArithmetic operation, int expected)
        {
            m_Effect.DurationKind = EffectDurationKind.Instant;
            m_Effect.DurationFrames = 0;
            m_Effect.Nodes = new[]
            {
                new EffectValueNode { NodeId = 1, Kind = kind, AttributeId = 101 },
                new EffectValueNode { NodeId = 2, Constant = "2.0000" },
                new EffectValueNode { NodeId = 3, Kind = EffectValueKind.Arithmetic, Arithmetic = operation, LeftNodeId = 1, RightNodeId = 2 }
            };
            m_Effect.Modifiers[0].Operation = AttributeOperation.Override;
            m_Effect.Modifiers[0].ValueNodeId = 3;
            AttributeSession session = new AttributeSession(Compile());
            session.BeginFrame(0);
            Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.True, error);
            Assert.That(Read(session, 2).Raw, Is.EqualTo(FP64.FromInt(expected).Raw));
        }

        [Test]
        public void ConstantZeroDivisorIsRejectedEvenWithDynamicNumerator()
        {
            m_Effect.Nodes = new[]
            {
                new EffectValueNode { NodeId = 1, Kind = EffectValueKind.SourceBase, AttributeId = 101 },
                new EffectValueNode { NodeId = 2, Constant = "0.0000" },
                new EffectValueNode { NodeId = 3, Kind = EffectValueKind.Arithmetic, Arithmetic = EffectArithmetic.Divide, LeftNodeId = 1, RightNodeId = 2 }
            };
            m_Effect.Modifiers[0].ValueNodeId = 3;
            Assert.That(Reject("StaticArithmetic").NodeId, Is.EqualTo(3));
        }

        [Test]
        public void CatalogOrderingDoesNotChangeCanonicalRuntimeState()
        {
            AttributeSession first = new AttributeSession(Compile());
            Array.Reverse(m_Catalog.Attributes);
            Array.Reverse(m_Setup.Entities);
            Array.Reverse(m_Profile.Attributes);
            AttributeSession second = new AttributeSession(Compile());
            first.BeginFrame(0);
            second.BeginFrame(0);
            first.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error);
            second.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out error);
            Assert.That(second.Runtime.CaptureState().GetCanonicalBytes(), Is.EqualTo(first.Runtime.CaptureState().GetCanonicalBytes()));
        }

        [Test]
        public void RuntimeSetupIsPlainObjectAndCompilationLocksEntityBindings()
        {
            Assert.That(typeof(UnityEngine.Object).IsAssignableFrom(typeof(AttributeSessionSetup)), Is.False);
            AttributeSession first = new AttributeSession(Compile());
            ulong id = ulong.MaxValue - 1;
            m_Setup.Entities[0].EntityId = id;
            m_Profile.Attributes[0].InitialBase = "50";
            AttributeSession second = new AttributeSession(Compile());
            Assert.That(first.Configuration.Entities.ContainsKey(1), Is.True);
            Assert.That(first.Configuration.Entities.ContainsKey(id), Is.False);
            Assert.That(second.Configuration.Entities.ContainsKey(1), Is.False);
            Assert.That(second.Runtime.TryGetController(new EntityId(id), out EffectController controller), Is.True);
            Assert.That(controller.EntityId.Value, Is.EqualTo(id));
            Assert.That(Read(first, 2).Raw, Is.EqualTo(FP64.FromInt(100).Raw));
            Assert.That(Read(second, 2).Raw, Is.EqualTo(FP64.FromInt(50).Raw));
        }

        [Test]
        public void NullRuntimeSetupDoesNotReplaceHostInitialization()
        {
            Assert.That(ConfigurationCompiler.TryCompileSession(null, out CompiledSessionConfiguration compiled,
                out IReadOnlyList<ConfigurationIssue> issues), Is.False);
            Assert.That(compiled, Is.Null);
            Assert.That(issues.Single().Code, Is.EqualTo("MissingSetup"));
            GameObject gameObject = new GameObject("AshACT 运行初始化测试");
            try
            {
                AttributeRuntimeHost host = gameObject.AddComponent<AttributeRuntimeHost>();
                Assert.That(host.TryInitialize(m_Setup, out issues), Is.True, string.Join("\n", issues));
                AttributeSession session = host.Session;
                Assert.That(host.TryInitialize(null, out issues), Is.False);
                Assert.That(host.Session, Is.SameAs(session));
                Assert.That(host.Setup, Is.SameAs(m_Setup));
                Assert.That(host.TryInitialize(out issues), Is.True, string.Join("\n", issues));
                Assert.That(host.Session, Is.Not.SameAs(session));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void EditorPreviewBuildsTemporaryInputsWithoutChangingAssets()
        {
            CharacterAttributeAsset other = New<CharacterAttributeAsset>();
            other.ConfigurationId = 2;
            other.Attributes = new[] { new CharacterAttributeEntry { Definition = m_Stat, InitialBase = "20" } };
            string[] before = m_Objects.Select(EditorJsonUtility.ToJson).ToArray();
            AttributeDebugWindow window = AttributeDebugWindow.OpenPreview(m_Catalog, m_Effect, m_Profile, other);
            try
            {
                AttributeSessionSetup setup = window.PreviewSetup;
                Assert.That(setup, Is.Not.SameAs(m_Setup));
                Assert.That(setup.Catalog, Is.SameAs(m_Catalog));
                Assert.That(setup.Effects.Single(), Is.SameAs(m_Effect));
                Assert.That(setup.Entities[0].Profile, Is.SameAs(m_Profile));
                Assert.That(setup.Entities[1].Profile, Is.SameAs(other));
                Assert.That(window.InitializePreview(setup), Is.True);
                Assert.That(window.Session.BeginFrame(0).Succeeded, Is.True);
                Assert.That(window.Session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.True, error);
                Assert.That(Read(window.Session, 2).Raw, Is.EqualTo(FP64.FromInt(30).Raw));
                Assert.That(window.InitializePreview(null), Is.False);
                Assert.That(Read(window.Session, 2).Raw, Is.EqualTo(FP64.FromInt(30).Raw));
                Assert.That(m_Objects.Select(EditorJsonUtility.ToJson).ToArray(), Is.EqualTo(before));
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void HostCreatesIndependentSessionAndPreservesItAfterInvalidCompilation()
        {
            GameObject gameObject = new GameObject("AshACT 配置测试");
            try
            {
                AttributeRuntimeHost host = gameObject.AddComponent<AttributeRuntimeHost>();
                Assert.That(host.TryInitialize(m_Setup, out IReadOnlyList<ConfigurationIssue> issues), Is.True);
                AttributeSession session = host.Session;
                m_Effect.SchemaVersion = 99;
                AttributeSessionSetup invalid = new AttributeSessionSetup
                {
                    Catalog = m_Catalog,
                    Entities = m_Setup.Entities,
                    Effects = m_Setup.Effects
                };
                Assert.That(host.TryInitialize(invalid, out issues), Is.False);
                Assert.That(host.Session, Is.SameAs(session));
                Assert.That(host.Setup, Is.SameAs(m_Setup));
                Assert.That(issues.Any(issue => issue.Code == "VersionMismatch"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SessionLocksValuesDefinitionsAndReleaseUntilNextInitialization()
        {
            CompiledSessionConfiguration compiled = Compile();
            AttributeSession old = new AttributeSession(compiled);
            m_Effect.Nodes[0].Constant = "2.0000";
            m_Profile.Attributes[0].InitialBase = "50.0000";
            foreach (VersionedConfigurationAsset asset in m_Objects.Cast<VersionedConfigurationAsset>())
            {
                asset.ReleaseVersion = 2;
            }

            AttributeSession next = new AttributeSession(Compile());
            old.BeginFrame(0);
            next.BeginFrame(0);
            Assert.That(old.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.True, error);
            Assert.That(next.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out error), Is.True, error);
            Assert.That(Read(old, 2).Raw, Is.EqualTo(FP64.FromInt(150).Raw));
            Assert.That(Read(next, 2).Raw, Is.EqualTo(FP64.FromInt(100).Raw));
            Assert.That(old.Configuration.ReleaseVersion, Is.EqualTo(1));
            Assert.That(next.Configuration.ReleaseVersion, Is.EqualTo(2));
            Assert.That(m_Profile.Attributes[0].InitialBase, Is.EqualTo("50.0000"));
        }

        [Test]
        public void RequiredParametersAndRuntimeCrossEntityCycleRemainAtomic()
        {
            m_Effect.Nodes[0].Kind = EffectValueKind.SourceCurrent;
            m_Effect.Nodes[0].AttributeId = 101;
            m_Effect.Modifiers[0].Operation = AttributeOperation.Add;
            m_Effect.RequiredParameters = new uint[] { 7 };
            AttributeSession session = new AttributeSession(Compile());
            session.BeginFrame(0);
            Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.False);
            EffectParameter[] parameters = { new EffectParameter(7, FP64.One) };
            Assert.That(session.TryApply(1001, 1, 2, parameters, out error), Is.True, error);
            string hash = session.Runtime.CaptureState().Hash;
            Assert.That(session.TryApply(1001, 2, 1, parameters, out error), Is.False);
            Assert.That(session.LastResult.Error, Is.EqualTo(EffectError.DependencyCycle));
            Assert.That(session.Runtime.CaptureState().Hash, Is.EqualTo(hash));
            Assert.That(RuntimeDiagnostics.Dependencies(session.Runtime.CaptureState()).Any(edge => edge.InstanceId.IsValid), Is.True);
            Assert.That(session.Deactivate(1).Succeeded, Is.True);
            EffectRuntimeState frozen = session.Runtime.CaptureState();
            Assert.That(frozen.Entities[1].Effects[0].IsSourceFrozen, Is.True);
            Assert.That(RuntimeDiagnostics.Dependencies(frozen).Any(edge => edge.InstanceId.IsValid), Is.False);
            Assert.That(session.Reactivate(1).Succeeded, Is.True);
        }

        [Test]
        public void PeriodicResourceUsesIntegerFrameExpiryBoundary()
        {
            m_Effect.PeriodFrames = 1;
            m_Effect.Modifiers[0].AttributeId = 102;
            m_Effect.Modifiers[0].Operation = AttributeOperation.Add;
            m_Effect.Nodes[0].Constant = "1.0000";
            AttributeSession session = new AttributeSession(Compile());
            for (int frame = 0; frame <= 3; frame++)
            {
                Assert.That(session.BeginFrame(frame).Succeeded, Is.True);
                if (frame == 0)
                {
                    Assert.That(session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error), Is.True, error);
                }

                Assert.That(session.EndFrame().Succeeded, Is.True);
            }

            session.Runtime.TryGetController(new EntityId(2), out EffectController controller);
            controller.Attributes.TryGetValue(new AttributeId(102), out AttributeValue value);
            Assert.That(value.BaseValue.Raw, Is.EqualTo(FP64.FromInt(82).Raw));
            Assert.That(controller.GetActiveEffects(), Is.Empty);
        }

        [Test]
        public void AggregatesAndOverrideWinnerReadRealModifiersWithoutWritingState()
        {
            AttributeSession session = new AttributeSession(Compile());
            session.BeginFrame(0);
            session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out string error);
            session.TryApply(1001, 1, 2, Array.Empty<EffectParameter>(), out error);
            session.Execute(2, new[]
            {
                new AttributeCommand(AttributeCommandKind.AddPersistent, new AttributeId(101), AttributeOperation.Divide, FP64.FromInt(2), 99),
                new AttributeCommand(AttributeCommandKind.AddPersistent, new AttributeId(101), AttributeOperation.Override, FP64.FromInt(7), 100, 4),
                new AttributeCommand(AttributeCommandKind.AddPersistent, new AttributeId(101), AttributeOperation.Override, FP64.FromInt(8), 101, 4)
            });
            EffectRuntimeState state = session.Runtime.CaptureState();
            AttributeAggregateView view = RuntimeDiagnostics.Aggregate(state.Entities[1].Attributes.Entries[0]);
            Assert.That(view.Multiplier.Raw, Is.EqualTo(FP64.FromInt(2).Raw));
            Assert.That(view.Divisor.Raw, Is.EqualTo(FP64.FromInt(2).Raw));
            Assert.That(view.OverrideHandle, Is.EqualTo(101));
            Assert.That(session.Runtime.CaptureState().Hash, Is.EqualTo(state.Hash));
        }

        [Test]
        public void RealAssetRoundTripAndEditorConnectionsCompileIntoDebugSession()
        {
            string folder = "Assets/AshACT/PhaseFourTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/AshACT", System.IO.Path.GetFileName(folder));
            EffectGraphWindow graph = null;
            AttributeDebugWindow debug = null;
            try
            {
                AttributeSessionSetup setup = ConfigurationSample.CreateAt(folder);
                string path = AssetDatabase.GetAssetPath(setup.Catalog);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                setup.Catalog = AssetDatabase.LoadAssetAtPath<AttributeCatalogAsset>(path);
                setup.Effects = setup.Effects.Select(effect => AssetDatabase.LoadAssetAtPath<EffectGraphAsset>(AssetDatabase.GetAssetPath(effect))).ToArray();
                foreach (EntityConfiguration entity in setup.Entities)
                {
                    entity.Profile = AssetDatabase.LoadAssetAtPath<CharacterAttributeAsset>(AssetDatabase.GetAssetPath(entity.Profile));
                }

                Assert.That(AssetDatabase.FindAssets("", new[] { folder }).Length, Is.EqualTo(5));
                Assert.That(AssetDatabase.LoadMainAssetAtPath(folder + "/SessionSetup.asset"), Is.Null);
                Assert.That(System.IO.File.ReadAllText(folder + "/Effect.asset"), Does.Not.Contain("\n  MarkerIds:"));
                Assert.That(setup.Entities[1].EntityId, Is.EqualTo(2));
                graph = EffectGraphWindow.Open(setup.Effects[0]);
                graph.AddNode(EffectValueKind.Constant);
                graph.Connect(4, 3, true);
                Assert.That(setup.Effects[0].Nodes[2].RightNodeId, Is.EqualTo(4));
                graph.Connect(2, 3, true);
                Array.Resize(ref setup.Effects[0].Nodes, 3);
                debug = AttributeDebugWindow.Open(setup);
                Assert.That(debug.InitializePreview(setup), Is.True);
                AttributeSession session = debug.Session;
                Assert.That(session.BeginFrame(0).Succeeded, Is.True);
                Assert.That(session.TryApply(1001, 1, 2, new[] { new EffectParameter(7, FP64.One) }, out string error), Is.True, error);
                Assert.That(Read(session, 2).Raw, Is.EqualTo(FP64.FromInt(200).Raw));
                setup.Effects[0].SchemaVersion = 99;
                Assert.That(debug.InitializePreview(setup), Is.False);
                Assert.That(debug.Session, Is.SameAs(session));
            }
            finally
            {
                if (graph != null)
                {
                    graph.Close();
                }

                if (debug != null)
                {
                    debug.Close();
                }

                AssetDatabase.DeleteAsset(folder);
            }
        }

        private T New<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            m_Objects.Add(asset);
            return asset;
        }

        private CompiledSessionConfiguration Compile()
        {
            Assert.That(ConfigurationCompiler.TryCompileSession(m_Setup, out CompiledSessionConfiguration compiled,
                out IReadOnlyList<ConfigurationIssue> issues), Is.True, string.Join("\n", issues));
            return compiled;
        }

        private ConfigurationIssue Reject(string code)
        {
            Assert.That(ConfigurationCompiler.TryCompileSession(m_Setup, out CompiledSessionConfiguration compiled,
                out IReadOnlyList<ConfigurationIssue> issues), Is.False);
            Assert.That(compiled, Is.Null);
            ConfigurationIssue issue = issues.FirstOrDefault(item => item.Code == code);
            Assert.That(issue, Is.Not.Null, string.Join("\n", issues));
            return issue;
        }

        private static FP64 Read(AttributeSession session, ulong entity)
        {
            session.Runtime.TryGetController(new EntityId(entity), out EffectController controller);
            controller.Attributes.TryGetValue(new AttributeId(101), out AttributeValue value);
            return value.CurrentValue;
        }
    }
}
