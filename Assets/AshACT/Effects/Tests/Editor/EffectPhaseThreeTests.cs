using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AshACT.Attributes;
using Framework.FixedPoint;
using NUnit.Framework;

namespace AshACT.Effects.Tests
{
    public sealed class EffectPhaseThreeTests
    {
        private static readonly EntityId A = new EntityId(10);
        private static readonly EntityId B = new EntityId(20);
        private static readonly EntityId C = new EntityId(30);
        private static readonly AttributeId X = new AttributeId(1);
        private static readonly AttributeId Y = new AttributeId(2);
        private static readonly MarkerId Mark = new MarkerId(1);

        [TestCase(EffectValueKind.SourceBase, 10)]
        [TestCase(EffectValueKind.SourceCurrent, 15)]
        [TestCase(EffectValueKind.TargetBase, 100)]
        [TestCase(EffectValueKind.TargetCurrent, 105)]
        public void ReadKindsAreExplicitAndInstantReadsCurrentValues(EffectValueKind kind, int expected)
        {
            EffectRuntime runtime = World(out EffectController a, out EffectController b);
            Begin(runtime, 0);
            Apply(runtime, A, Spec(1, Constant(5), A));
            Apply(runtime, B, Spec(2, Constant(5), A));
            EffectSpec instant = Spec(3, Read(kind), A, EffectDurationKind.Instant, attribute: Y);
            Apply(runtime, B, instant);
            Value(b, Y, expected, expected);
            Value(a, X, 10, 15);
        }

        [Test]
        public void PersistentSourceAndTargetReadsPropagateWithinTheSameCommand()
        {
            EffectRuntime runtime = World(out EffectController a, out EffectController b);
            Begin(runtime, 0);
            Apply(runtime, B, Spec(1, Read(EffectValueKind.SourceCurrent), A));
            Apply(runtime, C, Spec(2, Read(EffectValueKind.SourceCurrent), B));
            Apply(runtime, B, Spec(3, Read(EffectValueKind.TargetBase), A, attribute: Y));
            Set(runtime, A, X, 20);
            Value(a, X, 20, 20);
            Value(b, X, 100, 120);
            runtime.TryGetController(C, out EffectController c);
            Value(c, X, 1000, 1120);
            Set(runtime, B, X, 80);
            Value(b, Y, 0, 80);
            Value(c, X, 1000, 1100);
        }

        [Test]
        public void OperandUpdatesPreserveOverrideApplicationSequenceAndOldViews()
        {
            EffectRuntime runtime = World(out EffectController a, out EffectController b);
            Begin(runtime, 0);
            EffectResult first = Apply(runtime, B, Spec(1, Read(EffectValueKind.SourceBase), A,
                operation: AttributeOperation.Override));
            b.TryGetActive(first.InstanceId, out ActiveEffect old);
            Apply(runtime, B, Spec(2, Constant(50), A, operation: AttributeOperation.Override));
            ulong sequence = b.Attributes.LastApplicationSequence;
            Set(runtime, A, X, 30);
            Value(b, X, 100, 50);
            Assert.That(b.Attributes.LastApplicationSequence, Is.EqualTo(sequence));
            Assert.That(old.IsSourceFrozen, Is.False);
            Ok(runtime.TryDeactivate(A));
            b.TryGetActive(first.InstanceId, out ActiveEffect current);
            Assert.That(current.IsSourceFrozen, Is.True);
            Assert.That(old.IsSourceFrozen, Is.False);
            Assert.That(current.FrozenSource.Attributes.Entries[0].Value.BaseValue.Raw, Is.EqualTo(F(30).Raw));
            Value(a, X, 30, 30);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DirectAndCrossEntityCyclesRejectTheWholeEffectAndMarker(bool crossEntity)
        {
            EffectRuntime runtime = World(out _, out EffectController b);
            Begin(runtime, 0);
            if (crossEntity)
            {
                Apply(runtime, B, Spec(1, Read(EffectValueKind.SourceCurrent), A));
            }

            EffectRuntimeState before = runtime.CaptureState();
            EffectResult rejected = runtime.TryApply(crossEntity ? A : B,
                Spec(2, Read(crossEntity ? EffectValueKind.SourceCurrent : EffectValueKind.TargetCurrent), B));
            Assert.That(rejected.Error, Is.EqualTo(EffectError.DependencyCycle));
            Assert.That(runtime.CaptureState().GetCanonicalBytes(), Is.EqualTo(before.GetCanonicalBytes()));
            Assert.That(b.GetMarkerCount(Mark), Is.EqualTo(crossEntity ? 1 : 0));
        }

        [Test]
        public void ResourceMaximumParticipatesInCycleDetectionAndStablePropagation()
        {
            EffectRuntime runtime = new EffectRuntime();
            AttributeStore.TryCreate(new[]
            {
                Config(X, 100), new AttributeConfiguration(Y, F(90), FP64.Zero, F(1000), AttributeKind.Resource, X)
            }, out AttributeStore attributes);
            Ok(runtime.TryRegister(A, attributes, out EffectController controller));
            Begin(runtime, 0);
            EffectResult rejected = runtime.TryApply(A, Spec(1, Read(EffectValueKind.TargetBase, Y), A));
            Assert.That(rejected.Error, Is.EqualTo(EffectError.DependencyCycle));
            Set(runtime, A, X, 40);
            Value(controller, Y, 40, 40);
            Set(runtime, A, X, 100);
            Value(controller, Y, 40, 40);
        }

        [Test]
        public void BatchResourceTruncationIsVisibleToLaterCommandsAndDoesNotRestoreLostBase()
        {
            EffectRuntime runtime = new EffectRuntime();
            AttributeStore.TryCreate(new[]
            {
                Config(X, 100), new AttributeConfiguration(Y, F(90), FP64.Zero, F(1000), AttributeKind.Resource, X)
            }, out AttributeStore attributes);
            Ok(runtime.TryRegister(A, attributes, out EffectController controller));
            Begin(runtime, 0);
            Ok(runtime.TryExecute(A, new[] { Override(X, 30), Override(X, 100) }));
            Value(controller, Y, 30, 30);
        }

        [Test]
        public void DownstreamExpressionFailureRollsBackEveryStoreAndCounter()
        {
            EffectRuntime runtime = World(out EffectController a, out EffectController b);
            Begin(runtime, 0);
            EffectValue expression = EffectValue.Arithmetic(EffectArithmetic.Divide, Constant(10),
                EffectValue.Arithmetic(EffectArithmetic.Subtract, Read(EffectValueKind.SourceBase), Constant(20)));
            Apply(runtime, B, Spec(1, expression, A));
            EffectRuntimeState before = runtime.CaptureState();
            EffectResult rejected = runtime.TryExecute(A, new[] { Override(Y, 7), Override(X, 20) });
            Assert.That(rejected.Error, Is.EqualTo(EffectError.AttributeFailure));
            Assert.That(rejected.TargetId, Is.EqualTo(B));
            Assert.That(rejected.AttributeResult.Error, Is.EqualTo(AttributeError.DivideByZero));
            Assert.That(runtime.CaptureState().Hash, Is.EqualTo(before.Hash));
            Value(a, Y, 0, 0);
            Value(b, X, 100, 99);
        }

        [Test]
        public void EffectApplicationAndRemovalRollBackTheirEntireDependentClosure()
        {
            EffectRuntime runtime = World(out _, out _);
            Begin(runtime, 0);
            EffectValue denominator = EffectValue.Arithmetic(EffectArithmetic.Subtract,
                Read(EffectValueKind.SourceCurrent), Constant(20));
            Apply(runtime, B, Spec(1, EffectValue.Arithmetic(EffectArithmetic.Divide, Constant(1), denominator), A));
            EffectRuntimeState before = runtime.CaptureState();
            Assert.That(runtime.TryApply(A, Spec(2, Constant(10), A)).Succeeded, Is.False);
            Assert.That(runtime.CaptureState().Hash, Is.EqualTo(before.Hash));
            EffectResult boost = Apply(runtime, A, Spec(3, Constant(5), A));
            Set(runtime, A, X, 20);
            before = runtime.CaptureState();
            Assert.That(runtime.TryRemove(A, boost.InstanceId).Succeeded, Is.False);
            Assert.That(runtime.CaptureState().Hash, Is.EqualTo(before.Hash));
        }

        [Test]
        public void StableTopologicalOrderIncludesEntityAttributeAndReadKind()
        {
            EffectRuntime runtime = World(out _, out _);
            Begin(runtime, 0);
            Apply(runtime, A, Spec(1, Read(EffectValueKind.SourceCurrent), C));
            Ok(runtime.TryGetDependencyOrder(out IReadOnlyList<DependencyNode> order));
            List<DependencyNode> nodes = order.ToList();
            Assert.That(nodes.IndexOf(new DependencyNode(C, X, AttributeReadKind.Current)),
                Is.LessThan(nodes.IndexOf(new DependencyNode(A, X, AttributeReadKind.Current))));
            Ok(runtime.TryGetDependencyOrder(out IReadOnlyList<DependencyNode> repeated));
            Assert.That(repeated, Is.EqualTo(order));
        }

        [Test]
        public void PeriodicExpressionsUseEachTriggerValueIncludingFrozenBindings()
        {
            EffectRuntime runtime = World(out _, out EffectController b);
            Begin(runtime, 0);
            EffectResult id = Apply(runtime, B, Spec(1, Read(EffectValueKind.SourceCurrent), A,
                EffectDurationKind.Finite, duration: 5, period: 1));
            End(runtime);
            Begin(runtime, 1);
            Set(runtime, A, X, 20);
            End(runtime);
            Value(b, X, 120, 120);
            Begin(runtime, 2);
            Set(runtime, A, X, 30);
            Ok(runtime.TryDeactivate(A));
            End(runtime);
            Value(b, X, 150, 150);
            Begin(runtime, 3);
            Ok(runtime.TryReactivate(A));
            Set(runtime, A, X, 99);
            End(runtime);
            Value(b, X, 180, 180);
            Begin(runtime, 4);
            End(runtime);
            Begin(runtime, 5);
            Assert.That(b.TryGetActive(id.InstanceId, out _), Is.False);
            Value(b, X, 210, 210);
            End(runtime);
        }

        [Test]
        public void DeathCapturesBeforeCleanupAndKeepsDistinctSnapshotsAcrossRevival()
        {
            EffectRuntime runtime = World(out EffectController a, out EffectController b);
            Begin(runtime, 0);
            Apply(runtime, A, Spec(1, Constant(5), A));
            EffectSpec outgoing = Spec(2, Read(EffectValueKind.SourceCurrent), A);
            EffectResult first = Apply(runtime, B, outgoing);
            Value(b, X, 100, 115);
            Ok(runtime.TryDeactivate(A));
            Value(a, X, 10, 10);
            Assert.That(a.ActiveCount, Is.Zero);
            Assert.That(a.GetMarkerCount(Mark), Is.Zero);
            b.TryGetActive(first.InstanceId, out ActiveEffect frozenFirst);
            Assert.That(frozenFirst.FrozenSource.Lifecycle, Is.EqualTo(1));
            Value(b, X, 100, 115);
            Ok(runtime.TryReactivate(A));
            Set(runtime, A, X, 30);
            EffectResult second = Apply(runtime, B, outgoing);
            Value(b, X, 100, 145);
            Ok(runtime.TryDeactivate(A));
            b.TryGetActive(second.InstanceId, out ActiveEffect frozenSecond);
            Assert.That(frozenSecond.FrozenSource.Lifecycle, Is.EqualTo(2));
            Assert.That(frozenFirst.FrozenSource, Is.Not.SameAs(frozenSecond.FrozenSource));
            Assert.That(b.GetMarkerCount(Mark), Is.EqualTo(2));
            Ok(runtime.TryReactivate(A));
            Set(runtime, A, X, 80);
            Value(b, X, 100, 145);
            Ok(runtime.TryDeactivate(B));
            Assert.That(b.ActiveCount, Is.Zero);
            Assert.That(b.GetMarkerCount(Mark), Is.Zero);
            Ok(runtime.TryReactivate(B));
            Value(b, X, 100, 100);
        }

        [Test]
        public void FrozenSourceEdgesAreRemovedAndOldViewsAndSharedSnapshotRemainImmutable()
        {
            EffectRuntime runtime = World(out _, out EffectController b);
            Begin(runtime, 0);
            EffectResult first = Apply(runtime, B, Spec(1, Read(EffectValueKind.SourceCurrent), A));
            EffectResult second = Apply(runtime, C, Spec(2, Read(EffectValueKind.SourceCurrent), A));
            Ok(runtime.TryDeactivate(A));
            b.TryGetActive(first.InstanceId, out ActiveEffect bEffect);
            runtime.TryGetController(C, out EffectController c);
            c.TryGetActive(second.InstanceId, out ActiveEffect cEffect);
            Assert.That(bEffect.FrozenSource, Is.SameAs(cEffect.FrozenSource));
            Ok(runtime.TryReactivate(A));
            Apply(runtime, A, Spec(3, Read(EffectValueKind.SourceCurrent), B));
            Value(b, X, 100, 110);
        }

        [Test]
        public void FailedLifecycleCleanupRestoresLiveBindingAndEveryStore()
        {
            EffectRuntime runtime = new EffectRuntime();
            AttributeStore.TryCreate(new[]
            {
                Config(X, 10), new AttributeConfiguration(Y, F(5), F(5), F(1000), AttributeKind.Resource, X)
            }, out AttributeStore store);
            Ok(runtime.TryRegister(A, store, out _));
            AttributeStore.TryCreate(new[] { Config(X, 100) }, out AttributeStore target);
            Ok(runtime.TryRegister(B, target, out _));
            Begin(runtime, 0);
            Apply(runtime, A, Spec(1, Constant(10), A));
            Apply(runtime, B, Spec(2, Read(EffectValueKind.SourceCurrent), A));
            Set(runtime, A, X, 0);
            EffectRuntimeState before = runtime.CaptureState();
            Assert.That(runtime.TryDeactivate(A).Succeeded, Is.False);
            Assert.That(runtime.CaptureState().Hash, Is.EqualTo(before.Hash));
        }

        [Test]
        public void MissingReferencesParametersAndArithmeticFailuresAreStructuredAndAtomic()
        {
            EffectRuntime runtime = World(out _, out _);
            Begin(runtime, 0);
            EffectRuntimeState before = runtime.CaptureState();
            Assert.That(runtime.TryApply(B, Spec(1, Read(EffectValueKind.SourceCurrent), new EntityId(99))).Error,
                Is.EqualTo(EffectError.MissingSource));
            Assert.That(runtime.TryApply(B, Spec(2, Read(EffectValueKind.TargetCurrent, new AttributeId(99)), A)).Error,
                Is.EqualTo(EffectError.MissingAttribute));
            EffectValue parameter = new EffectValue(EffectValueKind.SetByCaller, parameterId: 99);
            EffectResult missing = runtime.TryApply(B, Spec(3,
                EffectValue.Arithmetic(EffectArithmetic.Add, Constant(1), parameter), A));
            Assert.That(missing.Error, Is.EqualTo(EffectError.MissingParameter));
            Assert.That(missing.ParameterId, Is.EqualTo(99));
            EffectValue overflow = EffectValue.Arithmetic(EffectArithmetic.Multiply, Constant(1000000000), Constant(1000000000));
            Assert.That(runtime.TryApply(B, Spec(4, overflow, A)).AttributeResult.Error, Is.EqualTo(AttributeError.Overflow));
            Assert.That(runtime.CaptureState().Hash, Is.EqualTo(before.Hash));
        }

        [TestCase(EffectArithmetic.Add, 5)]
        [TestCase(EffectArithmetic.Subtract, 1)]
        [TestCase(EffectArithmetic.Multiply, 6)]
        [TestCase(EffectArithmetic.Divide, 1)]
        public void ArithmeticNodesUseSafeFixedPointOperations(EffectArithmetic operation, int whole)
        {
            EffectRuntime runtime = World(out _, out EffectController b);
            Begin(runtime, 0);
            Apply(runtime, B, Spec(1, EffectValue.Arithmetic(operation, Constant(3), Constant(2)), A));
            b.Attributes.TryGetValue(X, out AttributeValue value);
            long expected = F(whole).Raw + (operation == EffectArithmetic.Divide ? FP64.HalfRaw : 0);
            Assert.That(value.CurrentValue.Raw, Is.EqualTo(F(100).Raw + expected));
        }

        [Test]
        public void NegativeDivisionAndFractionalMultiplicationKeepRawFloorSemantics()
        {
            EffectRuntime runtime = World(out _, out EffectController b);
            Begin(runtime, 0);
            EffectValue tiny = new EffectValue(EffectValueKind.Constant, FP64.FromRaw(-1));
            Apply(runtime, B, Spec(1, EffectValue.Arithmetic(EffectArithmetic.Divide, tiny, Constant(2)), A));
            b.Attributes.TryGetValue(X, out AttributeValue value);
            Assert.That(value.CurrentValue.Raw, Is.EqualTo(F(100).Raw - 1));
        }

        [Test]
        public void ExpressionSizeDepthAndInvalidOperatorsAreRejectedBeforeRuntime()
        {
            EffectValue value = Constant(1);
            for (int i = 0; i < 66; i++)
            {
                value = EffectValue.Arithmetic(EffectArithmetic.Add, value, Constant(1));
            }

            Assert.That(Create(1, value, out _).Error, Is.EqualTo(EffectError.InvalidDefinition));
            Assert.That(Create(1, EffectValue.Arithmetic((EffectArithmetic)99, Constant(1), Constant(1)), out _).Error,
                Is.EqualTo(EffectError.InvalidDefinition));
        }

        [Test]
        public void RuntimeOwnsWritesAfterStartAndEffectHandlesCannotBeRemovedExternally()
        {
            EffectRuntime runtime = World(out EffectController a, out _);
            Assert.That(new EffectRuntime().TryRegister(new EntityId(99), a.Attributes, out _).Error,
                Is.EqualTo(EffectError.StoreAlreadyRegistered));
            Begin(runtime, 0);
            EffectResult id = Apply(runtime, A, Spec(1, Constant(1), A));
            a.TryGetActive(id.InstanceId, out ActiveEffect active);
            Assert.That(a.Attributes.TryExecute(new[] { Override(X, 20) }).Error,
                Is.EqualTo(AttributeError.StoreOwnedByRuntime));
            Assert.That(runtime.TryExecute(A, new[]
            {
                new AttributeCommand(AttributeCommandKind.RemovePersistent, X, modifierHandle: active.ModifierHandles[0])
            }).Error, Is.EqualTo(EffectError.ModifierOwnedByEffect));
            Ok(runtime.TryDeactivate(A));
            Assert.That(runtime.TryApply(A, active.Spec).Error, Is.EqualTo(EffectError.EntityInactive));
            Assert.That(runtime.TryExecute(A, new[] { Override(X, 20) }).Error, Is.EqualTo(EffectError.EntityInactive));
        }

        [Test]
        public void MultiTargetCyclesRemainIndependentAndSortedByEntity()
        {
            EffectRuntime runtime = World(out _, out _);
            Begin(runtime, 0);
            IReadOnlyList<EffectResult> results = runtime.ApplyMany(new[] { C, A, B },
                Spec(1, Read(EffectValueKind.SourceCurrent), A));
            Assert.That(results.Select(result => result.TargetId), Is.EqualTo(new[] { A, B, C }));
            Assert.That(results[0].Error, Is.EqualTo(EffectError.DependencyCycle));
            Assert.That(results[1].InstanceId.Value, Is.EqualTo(1));
            Assert.That(results[2].InstanceId.Value, Is.EqualTo(2));
        }

        [Test]
        public void PeriodFailureStopsBeforeLaterInstancesAndPreservesAllDependentState()
        {
            EffectRuntime runtime = World(out _, out EffectController b);
            Begin(runtime, 0);
            EffectValue expression = EffectValue.Arithmetic(EffectArithmetic.Divide, Constant(1),
                Read(EffectValueKind.SourceBase));
            EffectResult period = Apply(runtime, B, Spec(1, expression, A, period: 1));
            Apply(runtime, C, Spec(2, Constant(5), A, period: 1));
            End(runtime);
            Begin(runtime, 1);
            Set(runtime, A, X, 0);
            EffectRuntimeState before = runtime.CaptureState();
            EffectResult failure = runtime.TryEndFrame();
            Assert.That(failure.AttributeResult.Error, Is.EqualTo(AttributeError.DivideByZero));
            Assert.That(failure.InstanceId, Is.EqualTo(period.InstanceId));
            Assert.That(runtime.Phase, Is.EqualTo(EffectFramePhase.Faulted));
            Assert.That(runtime.LastCompletedFrame, Is.Zero);
            Value(b, X, 100, 100);
            runtime.TryGetController(C, out EffectController c);
            Value(c, X, 1000, 1000);
            Assert.That(runtime.LastInstanceId, Is.EqualTo(before.LastInstanceId));
            Assert.That(runtime.TryDeactivate(A).Error, Is.EqualTo(EffectError.Faulted));
            Assert.That(runtime.TryReactivate(A).Error, Is.EqualTo(EffectError.Faulted));
            Assert.That(runtime.TryExecute(A, Array.Empty<AttributeCommand>()).Error, Is.EqualTo(EffectError.Faulted));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FullSaveRestoreContinuationMatchesEveryCanonicalByte(bool midInput)
        {
            EffectRuntime first = World(out _, out _);
            EffectSpec live = Spec(1, Read(EffectValueKind.SourceCurrent), A, EffectDurationKind.Finite, duration: 6);
            EffectSpec periodic = Spec(2, new EffectValue(EffectValueKind.SetByCaller, parameterId: 7), A,
                EffectDurationKind.Finite, duration: 7, period: 2, parameter: new EffectParameter(7, F(3)));
            Begin(first, 0);
            Apply(first, B, live);
            Apply(first, C, periodic);
            Ok(first.TryDeactivate(A));
            Ok(first.TryReactivate(A));
            Apply(first, B, live);
            if (!midInput)
            {
                End(first);
            }

            EffectRuntimeState saved = first.CaptureState();
            EffectRuntime restored = World(out _, out _);
            Ok(restored.TryRestoreState(saved, new[] { live.Definition, periodic.Definition }));
            Assert.That(restored.CaptureState().GetCanonicalBytes(), Is.EqualTo(saved.GetCanonicalBytes()));
            byte[] external = saved.GetCanonicalBytes();
            external[0] = 99;
            Assert.That(saved.GetCanonicalBytes()[0], Is.EqualTo(EffectRuntimeState.ProtocolVersion));
            if (midInput)
            {
                End(first);
                End(restored);
            }

            for (int frame = 1; frame <= 8; frame++)
            {
                foreach (EffectRuntime world in new[] { first, restored })
                {
                    Begin(world, frame);
                    if (frame == 1)
                    {
                        Set(world, A, X, 30);
                    }

                    if (frame == 2)
                    {
                        Ok(world.TryDeactivate(A));
                        Ok(world.TryReactivate(A));
                        Apply(world, B, live);
                    }

                    End(world);
                }

                Assert.That(restored.CaptureState().GetCanonicalBytes(), Is.EqualTo(first.CaptureState().GetCanonicalBytes()));
                Assert.That(restored.CaptureState().Hash, Is.EqualTo(first.CaptureState().Hash));
            }
        }

        [Test]
        public void RestoreRejectsConfigurationMismatchAtomicallyAndAcceptsEquivalentDefinitions()
        {
            EffectRuntime first = World(out _, out _);
            EffectSpec spec = Spec(1, Read(EffectValueKind.SourceCurrent), A);
            Begin(first, 0);
            Apply(first, B, spec);
            End(first);
            EffectRuntimeState saved = first.CaptureState();
            EffectRuntime target = World(out _, out _);
            string before = target.CaptureState().Hash;
            EffectDefinition changed = Spec(1, Constant(7), A).Definition;
            Assert.That(target.TryRestoreState(saved, new[] { changed }).Error, Is.EqualTo(EffectError.ConfigurationMismatch));
            Assert.That(target.TryRestoreState(saved, Array.Empty<EffectDefinition>()).Succeeded, Is.False);
            Assert.That(target.TryRestoreState(saved, new[] { spec.Definition, spec.Definition }).Succeeded, Is.False);
            Assert.That(target.TryRestoreState(null, new[] { spec.Definition }).Error, Is.EqualTo(EffectError.InvalidState));
            Assert.That(target.CaptureState().Hash, Is.EqualTo(before));
            Ok(target.TryRestoreState(saved, new[] { Spec(1, Read(EffectValueKind.SourceCurrent), A).Definition }));
            EffectRuntime wrong = World(out _, out _, initialA: 11);
            before = wrong.CaptureState().Hash;
            Assert.That(wrong.TryRestoreState(saved, new[] { spec.Definition }).Error, Is.EqualTo(EffectError.ConfigurationMismatch));
            Assert.That(wrong.CaptureState().Hash, Is.EqualTo(before));
        }

        [Test]
        public void FaultedStateRoundTripsWithoutResumingHalfAFrame()
        {
            EffectRuntime runtime = World(out _, out _);
            EffectSpec period = Spec(1, EffectValue.Arithmetic(EffectArithmetic.Divide, Constant(1),
                Read(EffectValueKind.SourceBase)), A, period: 1);
            Begin(runtime, 0);
            Apply(runtime, B, period);
            End(runtime);
            Begin(runtime, 1);
            Set(runtime, A, X, 0);
            Assert.That(runtime.TryEndFrame().Succeeded, Is.False);
            EffectRuntime restored = World(out _, out _);
            Ok(restored.TryRestoreState(runtime.CaptureState(), new[] { period.Definition }));
            Assert.That(restored.CaptureState().GetCanonicalBytes(), Is.EqualTo(runtime.CaptureState().GetCanonicalBytes()));
            Assert.That(restored.TryBeginFrame(2).Error, Is.EqualTo(EffectError.Faulted));
        }

        [Test]
        public void InitialAndEmptyStateAndExternalModifierSequencesRoundTrip()
        {
            EffectRuntime runtime = World(out EffectController a, out _);
            Assert.That(a.Attributes.TryExecute(new[]
            {
                new AttributeCommand(AttributeCommandKind.AddPersistent, X, operand: F(4), modifierHandle: 99)
            }).Succeeded, Is.True);
            EffectRuntimeState initial = runtime.CaptureState();
            EffectRuntime restored = World(out _, out _);
            Ok(restored.TryRestoreState(initial, Array.Empty<EffectDefinition>()));
            Assert.That(restored.CaptureState().Hash, Is.EqualTo(initial.Hash));
            Begin(runtime, 0);
            Begin(restored, 0);
            EffectSpec effect = Spec(1, Constant(1), A);
            Apply(runtime, A, effect);
            Apply(restored, A, effect);
            Assert.That(restored.CaptureState().GetCanonicalBytes(), Is.EqualTo(runtime.CaptureState().GetCanonicalBytes()));
            EffectRuntime empty = new EffectRuntime();
            Ok(empty.TryRestoreState(new EffectRuntime().CaptureState(), Array.Empty<EffectDefinition>()));
        }

        [Test]
        public void RepeatedRunsAndRegistrationOrderProduceIdenticalFullStates()
        {
            Assert.That(Replay(false), Is.EqualTo(Replay(true)));
            Assert.That(Replay(false), Is.EqualTo(Replay(false)));
        }

        [Test]
        public void ExpiryFailureRollsBackTheWholeClosureAndRoundTripsFaultState()
        {
            EffectRuntime runtime = World(out EffectController a, out EffectController b);
            Begin(runtime, 0);
            EffectSpec boost = Spec(1, Constant(10), A, EffectDurationKind.Finite, duration: 1);
            Apply(runtime, A, boost);
            EffectSpec dependent = Spec(2, EffectValue.Arithmetic(EffectArithmetic.Divide, Constant(10),
                EffectValue.Arithmetic(EffectArithmetic.Subtract, Read(EffectValueKind.SourceCurrent), Constant(10))), A);
            Apply(runtime, B, dependent);
            End(runtime);
            Assert.That(runtime.TryBeginFrame(1).Succeeded, Is.False);
            Assert.That(runtime.Phase, Is.EqualTo(EffectFramePhase.Faulted));
            Assert.That(runtime.LastCompletedFrame, Is.Zero);
            Value(a, X, 10, 20);
            Value(b, X, 100, 101);
            Assert.That(a.GetMarkerCount(Mark), Is.EqualTo(1));
            EffectRuntime restored = World(out _, out _);
            Ok(restored.TryRestoreState(runtime.CaptureState(), new[] { boost.Definition, dependent.Definition }));
            Assert.That(restored.CaptureState().Hash, Is.EqualTo(runtime.CaptureState().Hash));
        }

        [Test]
        public void EarlierSuccessfulPeriodSurvivesLaterFailureAndRestoresExactly()
        {
            EffectRuntime runtime = World(out _, out EffectController b);
            Begin(runtime, 0);
            EffectSpec first = Spec(1, Constant(5), A, period: 1);
            EffectSpec second = Spec(2, EffectValue.Arithmetic(EffectArithmetic.Divide, Constant(1),
                Read(EffectValueKind.SourceBase)), A, period: 1);
            Apply(runtime, B, first);
            Apply(runtime, C, second);
            End(runtime);
            Begin(runtime, 1);
            Set(runtime, A, X, 0);
            Assert.That(runtime.TryEndFrame().Succeeded, Is.False);
            Value(b, X, 105, 105);
            Assert.That(b.GetActiveEffects()[0].NextPeriodFrame, Is.EqualTo(2));
            EffectRuntime restored = World(out _, out _);
            Ok(restored.TryRestoreState(runtime.CaptureState(), new[] { first.Definition, second.Definition }));
            Assert.That(restored.CaptureState().GetCanonicalBytes(), Is.EqualTo(runtime.CaptureState().GetCanonicalBytes()));
        }

        [TestCase("CurrentFrame", -2L)]
        [TestCase("LastCompletedFrame", 100L)]
        [TestCase("LastInstanceId", 0UL)]
        [TestCase("LastModifierHandle", 0UL)]
        public void RestoreRejectsDamagedFrameAndCounterStateWithoutMutation(string property, object value)
        {
            EffectRuntime runtime = World(out _, out _);
            Begin(runtime, 0);
            EffectSpec spec = Spec(1, Read(EffectValueKind.SourceCurrent), A);
            Apply(runtime, B, spec);
            EffectRuntimeState damaged = runtime.CaptureState();
            FieldInfo field = typeof(EffectRuntimeState).GetField("<" + property + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(damaged, value);
            EffectRuntime target = World(out _, out _);
            string before = target.CaptureState().Hash;
            Assert.That(target.TryRestoreState(damaged, new[] { spec.Definition }).Succeeded, Is.False);
            Assert.That(target.CaptureState().Hash, Is.EqualTo(before));
        }

        [Test]
        public void PeriodWithoutFutureTickAndPureMarkerStateRestoreWithTheirTimers()
        {
            EffectRuntime runtime = World(out _, out _);
            EffectSpec noTick = Spec(1, Read(EffectValueKind.SourceBase), A, EffectDurationKind.Finite,
                duration: 2, period: 3);
            Ok(EffectDefinition.TryCreate(new EffectDefinitionId(2), EffectDurationKind.Infinite, 0, 0,
                new[] { Mark }, Array.Empty<EffectModifier>(), out EffectDefinition marker));
            Ok(EffectSpec.TryCreate(marker, A, Array.Empty<EffectParameter>(), out EffectSpec markerSpec));
            Begin(runtime, 0);
            Apply(runtime, B, noTick);
            Apply(runtime, C, markerSpec);
            End(runtime);
            EffectRuntime restored = World(out _, out _);
            Ok(restored.TryRestoreState(runtime.CaptureState(), new[] { noTick.Definition, marker }));
            for (int frame = 1; frame <= 3; frame++)
            {
                Begin(runtime, frame);
                Begin(restored, frame);
                End(runtime);
                End(restored);
                Assert.That(restored.CaptureState().GetCanonicalBytes(), Is.EqualTo(runtime.CaptureState().GetCanonicalBytes()));
            }
        }

        [Test]
        public void ExpressionParametersAreCopiedAndOutOfRangeDynamicOperandsRejectTheCommand()
        {
            EffectRuntime runtime = World(out _, out _);
            EffectValue parameter = new EffectValue(EffectValueKind.SetByCaller, parameterId: 7);
            EffectSpec spec = Spec(1, EffectValue.Arithmetic(EffectArithmetic.Multiply,
                Read(EffectValueKind.SourceBase), parameter), A, parameter: new EffectParameter(7, F(2)));
            Begin(runtime, 0);
            Apply(runtime, B, spec);
            EffectRuntimeState before = runtime.CaptureState();
            EffectResult result = runtime.TryExecute(A, new[] { Override(X, 1000000000) });
            Assert.That(result.AttributeResult.Error, Is.EqualTo(AttributeError.InvalidOperand));
            Assert.That(runtime.CaptureState().Hash, Is.EqualTo(before.Hash));
        }

        private static byte[] Replay(bool reverse)
        {
            EffectRuntime runtime = World(out _, out _, reverse: reverse);
            EffectSpec effect = Spec(1, Read(EffectValueKind.SourceCurrent), A, EffectDurationKind.Finite, 4);
            for (int frame = 0; frame < 7; frame++)
            {
                Begin(runtime, frame);
                if (frame < 2)
                {
                    runtime.ApplyMany(new[] { C, B }, effect);
                }

                if (frame == 2)
                {
                    Set(runtime, A, X, 25);
                    Ok(runtime.TryDeactivate(A));
                }

                if (frame == 3)
                {
                    Ok(runtime.TryReactivate(A));
                    Apply(runtime, B, effect);
                }

                End(runtime);
            }

            return runtime.CaptureState().GetCanonicalBytes();
        }

        private static EffectRuntime World(out EffectController a, out EffectController b,
            bool reverse = false, int initialA = 10)
        {
            EffectRuntime runtime = new EffectRuntime();
            EntityId[] entities = reverse ? new[] { C, B, A } : new[] { A, B, C };
            foreach (EntityId entity in entities)
            {
                int value = entity == A ? initialA : entity == B ? 100 : 1000;
                Assert.That(AttributeStore.TryCreate(new[] { Config(X, value), Config(Y, 0) }, out AttributeStore store).Succeeded, Is.True);
                Ok(runtime.TryRegister(entity, store, out _));
            }

            runtime.TryGetController(A, out a);
            runtime.TryGetController(B, out b);
            return runtime;
        }

        private static AttributeConfiguration Config(AttributeId id, int value)
        {
            return new AttributeConfiguration(id, F(value), F(-1000000000), F(1000000000));
        }

        private static EffectResult Create(uint id, EffectValue value, out EffectDefinition definition,
            EffectDurationKind kind = EffectDurationKind.Infinite, long duration = 0, long period = 0,
            AttributeOperation operation = AttributeOperation.Add, AttributeId attribute = default)
        {
            return EffectDefinition.TryCreate(new EffectDefinitionId(id), kind, duration, period, new[] { Mark },
                new[] { new EffectModifier(attribute.IsValid ? attribute : X, operation, value) }, out definition);
        }

        private static EffectSpec Spec(uint id, EffectValue value, EntityId source,
            EffectDurationKind kind = EffectDurationKind.Infinite, long duration = 0, long period = 0,
            AttributeOperation operation = AttributeOperation.Add, AttributeId attribute = default,
            EffectParameter? parameter = null)
        {
            Ok(Create(id, value, out EffectDefinition definition, kind, duration, period, operation, attribute));
            Ok(EffectSpec.TryCreate(definition, source, parameter.HasValue ? new[] { parameter.Value } :
                Array.Empty<EffectParameter>(), out EffectSpec spec));
            return spec;
        }

        private static EffectValue Constant(int value) => new EffectValue(EffectValueKind.Constant, F(value));
        private static FP64 F(int value) => FP64.FromInt(value);
        private static EffectValue Read(EffectValueKind kind, AttributeId attribute = default) =>
            new EffectValue(kind, attributeId: attribute.IsValid ? attribute : X);
        private static AttributeCommand Override(AttributeId id, int value) =>
            new AttributeCommand(AttributeCommandKind.Instant, id, AttributeOperation.Override, F(value));

        private static void Set(EffectRuntime runtime, EntityId entity, AttributeId id, int value)
        {
            Ok(runtime.TryExecute(entity, new[] { Override(id, value) }));
        }

        private static EffectResult Apply(EffectRuntime runtime, EntityId target, EffectSpec spec)
        {
            EffectResult result = runtime.TryApply(target, spec);
            Ok(result);
            return result;
        }

        private static void Begin(EffectRuntime runtime, int frame)
        {
            Ok(runtime.TryBeginFrame(frame));
        }

        private static void End(EffectRuntime runtime)
        {
            Ok(runtime.TryEndFrame());
        }

        private static void Ok(EffectResult result)
        {
            Assert.That(result.Succeeded, Is.True,
                $"{result.Error}/{result.AttributeResult.Error} target={result.TargetId} modifier={result.ModifierIndex}");
        }

        private static void Value(EffectController controller, AttributeId id, int baseValue, int current)
        {
            Assert.That(controller.Attributes.TryGetValue(id, out AttributeValue value), Is.True);
            Assert.That(value.BaseValue.Raw, Is.EqualTo(F(baseValue).Raw));
            Assert.That(value.CurrentValue.Raw, Is.EqualTo(F(current).Raw));
        }
    }
}
