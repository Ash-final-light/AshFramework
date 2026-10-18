using System;
using System.Collections.Generic;
using AshACT.Attributes;
using Framework.FixedPoint;
using NUnit.Framework;

namespace AshACT.Effects.Tests
{
    public sealed class EffectRuntimeTests
    {
        private static readonly AttributeId Stat = new AttributeId(1);
        private static readonly AttributeId Other = new AttributeId(2);
        private static readonly EntityId Target = new EntityId(10);
        private static readonly MarkerId Marker = new MarkerId(7);

        [Test]
        public void ReapplicationCreatesIndependentInstancesAndModifierHandles()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EffectSpec spec = Spec(Definition(EffectDurationKind.Finite, 3, modifiers: new[] { Add(10) }));
            Begin(runtime, 0);
            EffectResult first = Apply(runtime, spec);
            End(runtime);
            Begin(runtime, 1);
            EffectResult second = Apply(runtime, spec);
            Assert.That(first.InstanceId, Is.Not.EqualTo(second.InstanceId));
            Assert.That(controller.ActiveCount, Is.EqualTo(2));
            controller.TryGetActive(first.InstanceId, out ActiveEffect a);
            controller.TryGetActive(second.InstanceId, out ActiveEffect b);
            Assert.That(a.EndFrame, Is.EqualTo(3));
            Assert.That(b.EndFrame, Is.EqualTo(4));
            Assert.That(a.ModifierHandles[0], Is.Not.EqualTo(b.ModifierHandles[0]));
            Value(controller, 100, 120);
            End(runtime);
            Begin(runtime, 2);
            End(runtime);
            Begin(runtime, 3);
            Assert.That(controller.TryGetActive(first.InstanceId, out _), Is.False);
            Assert.That(controller.TryGetActive(second.InstanceId, out _), Is.True);
            Value(controller, 100, 110);
            End(runtime);
            Begin(runtime, 4);
            Value(controller, 100, 100);
            Assert.That(controller.ActiveCount, Is.Zero);
        }

        [Test]
        public void PureMarkersNeedNoAttributesAndCountPerInstanceUntilTheirOwnExpiry()
        {
            EffectRuntime runtime = Runtime(out EffectController controller, empty: true);
            EffectSpec spec = Spec(Definition(EffectDurationKind.Finite, 2, markers: new[] { Marker }));
            Begin(runtime, 0);
            Apply(runtime, spec);
            Apply(runtime, spec);
            Assert.That(controller.GetMarkerCount(Marker), Is.EqualTo(2));
            Assert.That(controller.Attributes.AttributeIds.Count, Is.Zero);
            Assert.That(runtime.LastModifierHandle, Is.Zero);
            End(runtime);
            Begin(runtime, 1);
            Apply(runtime, spec);
            End(runtime);
            Begin(runtime, 2);
            Assert.That(controller.GetMarkerCount(Marker), Is.EqualTo(1));
            End(runtime);
            Begin(runtime, 3);
            Assert.That(controller.GetMarkerCount(Marker), Is.Zero);
        }

        [Test]
        public void InfiniteMarkerSurvivesFramesAndRemovalIsExplicitAndScoped()
        {
            EffectRuntime runtime = Runtime(out EffectController controller, empty: true);
            Begin(runtime, 0);
            EffectResult applied = Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, markers: new[] { Marker })));
            controller.TryGetActive(applied.InstanceId, out ActiveEffect active);
            Assert.That(active.EndFrame, Is.Null);
            Assert.That(active.NextPeriodFrame, Is.Null);
            End(runtime);
            Begin(runtime, 1);
            Assert.That(runtime.TryRemove(new EntityId(99), applied.InstanceId).Error, Is.EqualTo(EffectError.MissingTarget));
            Assert.That(controller.GetMarkerCount(Marker), Is.EqualTo(1));
            Success(runtime.TryRemove(Target, applied.InstanceId));
            Assert.That(runtime.TryRemove(Target, applied.InstanceId).Error, Is.EqualTo(EffectError.MissingInstance));
            Assert.That(controller.GetMarkerCount(Marker), Is.Zero);
        }

        [Test]
        public void InstantEffectsChangeBaseAndLeaveNoActiveInstanceOrMarker()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EffectSpec spec = Spec(Definition(EffectDurationKind.Instant, markers: new[] { Marker },
                modifiers: new[] { Constant(AttributeOperation.Multiply, FP64.FromRaw(FP64.OneRaw + FP64.HalfRaw)) }));
            Begin(runtime, 0);
            EffectResult first = Apply(runtime, spec);
            EffectResult second = Apply(runtime, spec);
            Assert.That(second.InstanceId.Value, Is.EqualTo(first.InstanceId.Value + 1));
            Value(controller, 225, 225);
            Assert.That(controller.ActiveCount, Is.Zero);
            Assert.That(controller.GetMarkerCount(Marker), Is.Zero);
            Assert.That(runtime.LastModifierHandle, Is.Zero);
            Assert.That(runtime.TryRemove(Target, first.InstanceId).Error, Is.EqualTo(EffectError.MissingInstance));
        }

        [Test]
        public void RemovingPersistentEffectRecalculatesFromLatestBaseAndRemainingModifiers()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Begin(runtime, 0);
            EffectResult first = Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, modifiers: new[] { Add(10) })));
            Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, modifiers: new[] { Add(20) })));
            Apply(runtime, Spec(Definition(EffectDurationKind.Instant, modifiers: new[] { Add(5) })));
            Value(controller, 105, 135);
            Success(runtime.TryRemove(Target, first.InstanceId));
            Value(controller, 105, 125);
        }

        [Test]
        public void EqualPriorityOverridesUseApplicationOrderRatherThanDefinitionId()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Begin(runtime, 0);
            EffectSpec first = Spec(Definition(EffectDurationKind.Infinite,
                modifiers: new[] { Constant(AttributeOperation.Override, FP64.FromInt(10)) }, id: 999));
            EffectSpec second = Spec(Definition(EffectDurationKind.Infinite,
                modifiers: new[] { Constant(AttributeOperation.Override, FP64.FromInt(20)) }, id: 1));
            Apply(runtime, first);
            EffectResult last = Apply(runtime, second);
            Value(controller, 100, 20);
            Success(runtime.TryRemove(Target, last.InstanceId));
            Value(controller, 100, 10);
        }

        [TestCase(4, 2, 1)]
        [TestCase(5, 2, 2)]
        [TestCase(2, 2, 0)]
        [TestCase(1, 3, 0)]
        public void PeriodStartsAfterOnePeriodAndNeverFiresOnExpiry(int duration, int period, int ticks)
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EffectSpec spec = Spec(Definition(EffectDurationKind.Finite, duration, period,
                new[] { Marker }, new[] { Add(10) }));
            Begin(runtime, 0);
            EffectResult applied = Apply(runtime, spec);
            Value(controller, 100, 100);
            Assert.That(controller.GetMarkerCount(Marker), Is.EqualTo(1));
            controller.TryGetActive(applied.InstanceId, out ActiveEffect active);
            Assert.That(active.ModifierHandles.Count, Is.Zero);
            End(runtime);
            for (int frame = 1; frame <= duration; frame++)
            {
                Begin(runtime, frame);
                if (frame == duration)
                {
                    Assert.That(controller.ActiveCount, Is.Zero);
                    Assert.That(controller.GetMarkerCount(Marker), Is.Zero);
                }

                End(runtime);
            }

            Value(controller, 100 + ticks * 10, 100 + ticks * 10);
        }

        [Test]
        public void InfinitePeriodUsesResolvedParametersAndOldViewsRemainUnchanged()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EffectSpec spec = Spec(Definition(EffectDurationKind.Infinite, period: 1,
                modifiers: new[] { Caller(8) }), new EffectParameter(8, FP64.FromInt(3)));
            Begin(runtime, 0);
            EffectResult applied = Apply(runtime, spec);
            controller.TryGetActive(applied.InstanceId, out ActiveEffect before);
            End(runtime);
            Begin(runtime, 1);
            End(runtime);
            Value(controller, 103, 103);
            controller.TryGetActive(applied.InstanceId, out ActiveEffect after);
            Assert.That(before.NextPeriodFrame, Is.EqualTo(1));
            Assert.That(after.NextPeriodFrame, Is.EqualTo(2));
            Begin(runtime, 2);
            Success(runtime.TryRemove(Target, applied.InstanceId));
            End(runtime);
            Value(controller, 103, 103);
        }

        [Test]
        public void InputIsVisibleToPeriodsAndPeriodsExecuteInInstanceOrder()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Begin(runtime, 0);
            Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, period: 1,
                modifiers: new[] { Add(10) }, id: 99)));
            Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, period: 1,
                modifiers: new[] { Constant(AttributeOperation.Multiply, FP64.FromInt(2)) }, id: 1)));
            End(runtime);
            Begin(runtime, 1);
            Apply(runtime, Spec(Definition(EffectDurationKind.Instant, modifiers: new[] { Add(5) })));
            Value(controller, 105, 105);
            End(runtime);
            Value(controller, 230, 230);
        }

        [Test]
        public void ExpiryRunsBeforeNewInputAndNewInstancesDoNotRefreshOldOnes()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EffectSpec spec = Spec(Definition(EffectDurationKind.Finite, 1, markers: new[] { Marker }, modifiers: new[] { Add(10) }));
            Begin(runtime, 0);
            EffectResult old = Apply(runtime, spec);
            End(runtime);
            Begin(runtime, 1);
            Assert.That(controller.GetMarkerCount(Marker), Is.Zero);
            Value(controller, 100, 100);
            EffectResult replacement = Apply(runtime, spec);
            Assert.That(replacement.InstanceId, Is.Not.EqualTo(old.InstanceId));
            Assert.That(controller.GetMarkerCount(Marker), Is.EqualTo(1));
        }

        [TestCase(EffectDurationKind.Instant, 0)]
        [TestCase(EffectDurationKind.Finite, 0)]
        [TestCase(EffectDurationKind.Finite, 1)]
        [TestCase(EffectDurationKind.Infinite, 0)]
        [TestCase(EffectDurationKind.Infinite, 1)]
        public void MissingAttributeRejectsEntireEffectIncludingMarkers(EffectDurationKind kind, int period)
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EffectSpec spec = Spec(Definition(kind, kind == EffectDurationKind.Finite ? 3 : 0, period,
                new[] { Marker }, new[] { Add(10), new EffectModifier(Other, AttributeOperation.Add,
                    new EffectValue(EffectValueKind.Constant, FP64.One)) }));
            Begin(runtime, 0);
            EffectResult result = runtime.TryApply(Target, spec);
            Assert.That(result.Error, Is.EqualTo(EffectError.MissingAttribute));
            Assert.That(result.AttributeId, Is.EqualTo(Other));
            Assert.That(result.ModifierIndex, Is.EqualTo(1));
            NoCommit(runtime, controller);
        }

        [TestCase(EffectDurationKind.Instant, 0)]
        [TestCase(EffectDurationKind.Finite, 0)]
        [TestCase(EffectDurationKind.Finite, 1)]
        [TestCase(EffectDurationKind.Infinite, 0)]
        [TestCase(EffectDurationKind.Infinite, 1)]
        public void MissingCallerRejectsEntireEffectIncludingMarkers(EffectDurationKind kind, int period)
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Begin(runtime, 0);
            EffectResult result = runtime.TryApply(Target, Spec(Definition(kind,
                kind == EffectDurationKind.Finite ? 3 : 0, period, new[] { Marker }, new[] { Add(10), Caller(42) })));
            Assert.That(result.Error, Is.EqualTo(EffectError.MissingParameter));
            Assert.That(result.ParameterId, Is.EqualTo(42));
            Assert.That(result.ModifierIndex, Is.EqualTo(1));
            NoCommit(runtime, controller);
        }

        [TestCase(AttributeOperation.Multiply, -1)]
        [TestCase(AttributeOperation.Divide, 0)]
        [TestCase(AttributeOperation.Divide, -1)]
        public void InvalidCallerOperandsFailBeforeRegistration(AttributeOperation operation, int amount)
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Begin(runtime, 0);
            EffectResult result = runtime.TryApply(Target, Spec(Definition(EffectDurationKind.Infinite, period: 1,
                markers: new[] { Marker }, modifiers: new[] { Caller(1, operation) }), new EffectParameter(1, FP64.FromInt(amount))));
            Assert.That(result.Error, Is.EqualTo(EffectError.InvalidParameter));
            NoCommit(runtime, controller);
        }

        [Test]
        public void AttributeTransactionFailureRollsBackModifiersAndCounters()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Begin(runtime, 0);
            EffectResult result = runtime.TryApply(Target, Spec(Definition(EffectDurationKind.Infinite,
                markers: new[] { Marker }, modifiers: new[] { Add(10),
                    Constant(AttributeOperation.Multiply, FP64.FromInt(1000000000)) })));
            Assert.That(result.Error, Is.EqualTo(EffectError.AttributeFailure));
            Assert.That(result.AttributeResult.Error, Is.EqualTo(AttributeError.Overflow));
            NoCommit(runtime, controller);
            Assert.That(Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, modifiers: new[] { Add(1) }))).InstanceId.Value,
                Is.EqualTo(1));
            Assert.That(runtime.LastModifierHandle, Is.EqualTo(1));
        }

        [Test]
        public void ResourceRejectsPersistentButAcceptsInstantAndPeriodicChanges()
        {
            EffectRuntime runtime = new EffectRuntime();
            Success(runtime.TryRegister(Target, Store(new AttributeConfiguration(Stat, FP64.FromInt(100),
                FP64.Zero, FP64.FromInt(1000), AttributeKind.Resource)), out EffectController controller));
            Begin(runtime, 0);
            EffectResult failure = runtime.TryApply(Target, Spec(Definition(EffectDurationKind.Infinite,
                markers: new[] { Marker }, modifiers: new[] { Add(10) })));
            Assert.That(failure.AttributeResult.Error, Is.EqualTo(AttributeError.ResourceModifierNotAllowed));
            NoCommit(runtime, controller);
            Apply(runtime, Spec(Definition(EffectDurationKind.Instant, modifiers: new[] { Add(-5) })));
            Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, period: 1, modifiers: new[] { Add(-10) })));
            End(runtime);
            Begin(runtime, 1);
            End(runtime);
            Value(controller, 85, 85);
        }

        [Test]
        public void MultipleTargetsSortAndDeduplicateAndFailuresRemainIndependent()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EntityId missing = new EntityId(20);
            EntityId last = new EntityId(30);
            Success(runtime.TryRegister(missing, Store(), out EffectController empty));
            Success(runtime.TryRegister(last, Store(Config(100)), out EffectController other));
            Begin(runtime, 0);
            IReadOnlyList<EffectResult> results = runtime.ApplyMany(new[] { last, missing, Target, last },
                Spec(Definition(EffectDurationKind.Infinite, markers: new[] { Marker }, modifiers: new[] { Add(10) })));
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].TargetId, Is.EqualTo(Target));
            Assert.That(results[0].InstanceId.Value, Is.EqualTo(1));
            Assert.That(results[1].TargetId, Is.EqualTo(missing));
            Assert.That(results[1].Error, Is.EqualTo(EffectError.MissingAttribute));
            Assert.That(results[2].TargetId, Is.EqualTo(last));
            Assert.That(results[2].InstanceId.Value, Is.EqualTo(2));
            Value(controller, 100, 110);
            Value(other, 100, 110);
            Assert.That(empty.GetMarkerCount(Marker), Is.Zero);
        }

        [Test]
        public void GeneratedHandlesSkipExistingStoreHandlesWithoutRemovingThem()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Assert.That(controller.Attributes.TryExecute(new[]
            {
                new AttributeCommand(AttributeCommandKind.AddPersistent, Stat, operand: FP64.FromInt(3), modifierHandle: 1)
            }).Succeeded, Is.True);
            Begin(runtime, 0);
            EffectResult result = Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, modifiers: new[] { Add(10), Add(20) })));
            controller.TryGetActive(result.InstanceId, out ActiveEffect active);
            Assert.That(active.ModifierHandles, Is.EqualTo(new ulong[] { 2, 3 }));
            Success(runtime.TryRemove(Target, result.InstanceId));
            Assert.That(controller.Attributes.HasModifier(1), Is.True);
            Value(controller, 100, 103);
        }

        [Test]
        public void PeriodFailureIsAtomicAndStopsFurtherFrameMutation()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Begin(runtime, 0);
            EffectResult applied = Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, period: 1,
                markers: new[] { Marker }, modifiers: new[] { Add(10), Constant(AttributeOperation.Multiply, FP64.FromInt(1000000000)) })));
            End(runtime);
            Begin(runtime, 1);
            EffectResult error = runtime.TryEndFrame();
            Assert.That(error.Error, Is.EqualTo(EffectError.AttributeFailure));
            Assert.That(error.InstanceId, Is.EqualTo(applied.InstanceId));
            Assert.That(error.AttributeResult.Error, Is.EqualTo(AttributeError.Overflow));
            Value(controller, 100, 100);
            Assert.That(runtime.Phase, Is.EqualTo(EffectFramePhase.Faulted));
            Assert.That(runtime.LastCompletedFrame, Is.Zero);
            Assert.That(runtime.Fault.Error, Is.EqualTo(error.Error));
            controller.TryGetActive(applied.InstanceId, out ActiveEffect active);
            Assert.That(active.NextPeriodFrame, Is.EqualTo(1));
            Assert.That(runtime.TryBeginFrame(2).Error, Is.EqualTo(EffectError.Faulted));
            Assert.That(runtime.TryEndFrame().Error, Is.EqualTo(EffectError.Faulted));
            Assert.That(runtime.TryRemove(Target, applied.InstanceId).Error, Is.EqualTo(EffectError.Faulted));
            Assert.That(runtime.TryApply(Target, active.Spec).Error, Is.EqualTo(EffectError.Faulted));
        }

        [Test]
        public void FailedRemovalKeepsWholeEffectAndExpiryFailureStopsFrame()
        {
            EffectRuntime runtime = new EffectRuntime();
            AttributeId resource = Other;
            Success(runtime.TryRegister(Target, Store(Config(100), new AttributeConfiguration(resource,
                FP64.FromInt(50), FP64.FromInt(50), FP64.FromInt(1000), AttributeKind.Resource, Stat)), out EffectController controller));
            Begin(runtime, 0);
            EffectResult applied = Apply(runtime, Spec(Definition(EffectDurationKind.Finite, 1,
                markers: new[] { Marker }, modifiers: new[] { Add(100) })));
            Apply(runtime, Spec(Definition(EffectDurationKind.Instant,
                modifiers: new[] { Constant(AttributeOperation.Override, FP64.Zero) })));
            EffectResult failed = runtime.TryRemove(Target, applied.InstanceId);
            Assert.That(failed.Error, Is.EqualTo(EffectError.AttributeFailure));
            Assert.That(controller.GetMarkerCount(Marker), Is.EqualTo(1));
            Value(controller, 0, 100);
            End(runtime);
            EffectResult expired = runtime.TryBeginFrame(1);
            Assert.That(expired.Error, Is.EqualTo(EffectError.AttributeFailure));
            Assert.That(runtime.Phase, Is.EqualTo(EffectFramePhase.Faulted));
            Assert.That(controller.ActiveCount, Is.EqualTo(1));
            Assert.That(controller.GetMarkerCount(Marker), Is.EqualTo(1));
            Value(controller, 0, 100);
        }

        [Test]
        public void FrameProtocolRejectsSkippedDuplicateOrOutOfPhaseCommands()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EffectSpec spec = Spec(Definition(EffectDurationKind.Instant));
            Assert.That(runtime.TryApply(Target, spec).Error, Is.EqualTo(EffectError.InvalidPhase));
            Assert.That(runtime.TryEndFrame().Error, Is.EqualTo(EffectError.InvalidPhase));
            Assert.That(runtime.TryBeginFrame(-1).Error, Is.EqualTo(EffectError.InvalidFrame));
            Assert.That(runtime.TryBeginFrame(1).Error, Is.EqualTo(EffectError.InvalidFrame));
            Begin(runtime, 0);
            Assert.That(runtime.TryBeginFrame(0).Error, Is.EqualTo(EffectError.InvalidPhase));
            Assert.That(runtime.TryRegister(new EntityId(2), Store(), out _).Error, Is.EqualTo(EffectError.InvalidPhase));
            End(runtime);
            Assert.That(runtime.TryBeginFrame(0).Error, Is.EqualTo(EffectError.InvalidFrame));
            Assert.That(runtime.TryBeginFrame(2).Error, Is.EqualTo(EffectError.InvalidFrame));
            Assert.That(runtime.TryApply(Target, spec).Error, Is.EqualTo(EffectError.InvalidPhase));
            Begin(runtime, 1);
            Assert.That(runtime.TryApply(Target, null).Error, Is.EqualTo(EffectError.InvalidSpec));
            NoCommit(runtime, controller);
        }

        [Test]
        public void EntitiesCannotAliasAStoreAndInvalidRegistrationDoesNotMutate()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Assert.That(runtime.TryRegister(Target, Store(), out _).Error, Is.EqualTo(EffectError.DuplicateEntity));
            Assert.That(runtime.TryRegister(new EntityId(20), controller.Attributes, out _).Error, Is.EqualTo(EffectError.StoreAlreadyRegistered));
            Assert.That(runtime.TryRegister(default, Store(), out _).Error, Is.EqualTo(EffectError.InvalidEntity));
            Assert.That(runtime.TryRegister(new EntityId(20), null, out _).Error, Is.EqualTo(EffectError.InvalidEntity));
            Assert.That(runtime.TryGetController(Target, out EffectController found), Is.True);
            Assert.That(found, Is.SameAs(controller));
        }

        [Test]
        public void FiniteFrameOverflowRejectsEffectWithoutMutation()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Begin(runtime, 0);
            End(runtime);
            Begin(runtime, 1);
            EffectResult error = runtime.TryApply(Target, Spec(Definition(EffectDurationKind.Finite, long.MaxValue,
                markers: new[] { Marker }, modifiers: new[] { Add(10) })));
            Assert.That(error.Error, Is.EqualTo(EffectError.FrameOverflow));
            NoCommit(runtime, controller);
        }

        [Test]
        public void PeriodBeyondRepresentableFrameHasNoWrappedSchedule()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            Begin(runtime, 0);
            End(runtime);
            Begin(runtime, 1);
            EffectResult applied = Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, period: long.MaxValue,
                modifiers: new[] { Add(10) })));
            controller.TryGetActive(applied.InstanceId, out ActiveEffect active);
            Assert.That(active.NextPeriodFrame, Is.Null);
            Assert.That(active.EndFrame, Is.Null);
            End(runtime);
            Value(controller, 100, 100);
        }

        [Test]
        public void RepeatedSimulationProducesSameRawIdsHandlesMarkersAndSchedule()
        {
            Assert.That(Replay(), Is.EqualTo(Replay()));
        }

        [Test]
        public void PeriodAndDurationAreRelativeToTheActualApplicationFrame()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            for (int frame = 0; frame < 2; frame++)
            {
                Begin(runtime, frame);
                End(runtime);
            }

            Begin(runtime, 2);
            EffectResult applied = Apply(runtime, Spec(Definition(EffectDurationKind.Finite, 5, 2,
                modifiers: new[] { Add(10) })));
            controller.TryGetActive(applied.InstanceId, out ActiveEffect active);
            Assert.That(active.StartFrame, Is.EqualTo(2));
            Assert.That(active.EndFrame, Is.EqualTo(7));
            Assert.That(active.NextPeriodFrame, Is.EqualTo(4));
            End(runtime);
            for (int frame = 3; frame <= 7; frame++)
            {
                Begin(runtime, frame);
                End(runtime);
                int ticks = frame < 4 ? 0 : frame < 6 ? 1 : 2;
                Value(controller, 100 + ticks * 10, 100 + ticks * 10);
            }

            Assert.That(controller.ActiveCount, Is.Zero);
        }

        [Test]
        public void SpecsSharingDefinitionRetainIndependentCallerValues()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EffectDefinition definition = Definition(EffectDurationKind.Infinite,
                modifiers: new[] { Caller(1) });
            Begin(runtime, 0);
            EffectResult first = Apply(runtime, Spec(definition, new EffectParameter(1, FP64.FromInt(5))));
            EffectResult second = Apply(runtime, Spec(definition, new EffectParameter(1, FP64.FromInt(20))));
            Value(controller, 100, 125);
            Success(runtime.TryRemove(Target, second.InstanceId));
            Value(controller, 100, 105);
            Assert.That(controller.TryGetActive(first.InstanceId, out _), Is.True);
        }

        [Test]
        public void GlobalPeriodOrderUsesInstanceIdAndStopsBeforeLaterTargetsOnFailure()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            EntityId otherId = new EntityId(20);
            Success(runtime.TryRegister(otherId, Store(Config(100)), out EffectController other));
            Begin(runtime, 0);
            EffectResult earliest = runtime.TryApply(otherId, Spec(Definition(EffectDurationKind.Infinite, period: 1,
                modifiers: new[] { Constant(AttributeOperation.Multiply, FP64.FromInt(1000000000)) })));
            Success(earliest);
            Apply(runtime, Spec(Definition(EffectDurationKind.Infinite, period: 1, modifiers: new[] { Add(5) })));
            End(runtime);
            Begin(runtime, 1);
            EffectResult error = runtime.TryEndFrame();
            Assert.That(error.TargetId, Is.EqualTo(otherId));
            Assert.That(error.InstanceId, Is.EqualTo(earliest.InstanceId));
            Assert.That(runtime.Phase, Is.EqualTo(EffectFramePhase.Faulted));
            Value(controller, 100, 100);
            Value(other, 100, 100);
        }

        private static string Replay()
        {
            EffectRuntime runtime = Runtime(out EffectController controller);
            List<string> states = new List<string>();
            EffectSpec periodic = Spec(Definition(EffectDurationKind.Finite, 4, 1, new[] { Marker }, new[] { Add(3) }));
            EffectSpec persistent = Spec(Definition(EffectDurationKind.Finite, 3, modifiers: new[] { Add(7) }));
            for (int frame = 0; frame <= 5; frame++)
            {
                Begin(runtime, frame);
                if (frame < 2)
                {
                    Apply(runtime, periodic);
                    Apply(runtime, persistent);
                }

                End(runtime);
                controller.Attributes.TryGetValue(Stat, out AttributeValue value);
                states.Add($"{frame}:{value.BaseValue.Raw}:{value.CurrentValue.Raw}:{runtime.LastInstanceId}:{runtime.LastModifierHandle}:{controller.GetMarkerCount(Marker)}");
                foreach (ActiveEffect active in controller.GetActiveEffects())
                {
                    states.Add($"{active.Id}:{active.StartFrame}:{active.EndFrame}:{active.NextPeriodFrame}:{string.Join(",", active.ModifierHandles)}");
                }
            }

            return string.Join(";", states);
        }

        private static EffectRuntime Runtime(out EffectController controller, bool empty = false)
        {
            EffectRuntime runtime = new EffectRuntime();
            Success(runtime.TryRegister(Target, empty ? Store() : Store(Config(100)), out controller));
            return runtime;
        }

        private static AttributeConfiguration Config(int value)
        {
            return new AttributeConfiguration(Stat, FP64.FromInt(value), FP64.Zero, FP64.FromInt(1000000000));
        }

        private static AttributeStore Store(params AttributeConfiguration[] configurations)
        {
            Assert.That(AttributeStore.TryCreate(configurations, out AttributeStore store).Succeeded, Is.True);
            return store;
        }

        private static EffectDefinition Definition(EffectDurationKind kind, long duration = 0, long period = 0,
            MarkerId[] markers = null, EffectModifier[] modifiers = null, uint id = 1)
        {
            Success(EffectDefinition.TryCreate(new EffectDefinitionId(id), kind, duration, period,
                markers ?? Array.Empty<MarkerId>(), modifiers ?? Array.Empty<EffectModifier>(), out EffectDefinition definition));
            return definition;
        }

        private static EffectSpec Spec(EffectDefinition definition, params EffectParameter[] parameters)
        {
            Success(EffectSpec.TryCreate(definition, new EntityId(1), parameters, out EffectSpec spec));
            return spec;
        }

        private static EffectModifier Add(int value)
        {
            return Constant(AttributeOperation.Add, FP64.FromInt(value));
        }

        private static EffectModifier Constant(AttributeOperation operation, FP64 value)
        {
            return new EffectModifier(Stat, operation, new EffectValue(EffectValueKind.Constant, value));
        }

        private static EffectModifier Caller(uint parameter, AttributeOperation operation = AttributeOperation.Add)
        {
            return new EffectModifier(Stat, operation, new EffectValue(EffectValueKind.SetByCaller, parameterId: parameter));
        }

        private static EffectResult Apply(EffectRuntime runtime, EffectSpec spec)
        {
            EffectResult result = runtime.TryApply(Target, spec);
            Success(result);
            return result;
        }

        private static void Begin(EffectRuntime runtime, long frame)
        {
            Success(runtime.TryBeginFrame(frame));
        }

        private static void End(EffectRuntime runtime)
        {
            Success(runtime.TryEndFrame());
        }

        private static void Success(EffectResult result)
        {
            Assert.That(result.Succeeded, Is.True, $"{result.Error}/{result.AttributeResult.Error}");
        }

        private static void Value(EffectController controller, int baseValue, int current)
        {
            Assert.That(controller.Attributes.TryGetValue(Stat, out AttributeValue value), Is.True);
            Assert.That(value.BaseValue.Raw, Is.EqualTo(FP64.FromInt(baseValue).Raw));
            Assert.That(value.CurrentValue.Raw, Is.EqualTo(FP64.FromInt(current).Raw));
        }

        private static void NoCommit(EffectRuntime runtime, EffectController controller)
        {
            Value(controller, 100, 100);
            Assert.That(controller.ActiveCount, Is.Zero);
            Assert.That(controller.GetMarkerCount(Marker), Is.Zero);
            Assert.That(runtime.LastInstanceId, Is.Zero);
            Assert.That(runtime.LastModifierHandle, Is.Zero);
            Assert.That(controller.Attributes.LastApplicationSequence, Is.Zero);
        }
    }
}
