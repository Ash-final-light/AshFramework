using System;
using System.Collections.Generic;
using System.Linq;
using AshACT.Attributes;
using Framework.FixedPoint;
using NUnit.Framework;

namespace AshACT.Effects.Tests
{
    public sealed class AttributeNotificationTests
    {
        private static readonly EntityId A = new EntityId(10);
        private static readonly EntityId B = new EntityId(20);
        private static readonly EntityId C = new EntityId(30);
        private static readonly AttributeId X = new AttributeId(1);
        private static readonly AttributeId Y = new AttributeId(2);

        [TestCase(AttributeChangeChannels.Base, 1)]
        [TestCase(AttributeChangeChannels.Current, 0)]
        [TestCase(AttributeChangeChannels.Both, 1)]
        public void MaskedBaseChangesRespectChannels(AttributeChangeChannels channels, int expected)
        {
            EffectRuntime runtime = World();
            Ok(runtime.TryBeginFrame(0));
            Ok(runtime.TryApply(A, Spec(1, Constant(50), A, operation: AttributeOperation.Override)));
            List<AttributeChange> changes = new List<AttributeChange>();
            using (runtime.Subscribe(A, X, changes.Add, channels))
            {
                Set(runtime, A, 20);
            }

            Assert.That(changes.Count, Is.EqualTo(expected));
            if (expected != 0)
            {
                Assert.That(changes[0].ChangedChannels, Is.EqualTo(AttributeChangeChannels.Base));
                Assert.That(changes[0].OldValue.BaseValue.Raw, Is.EqualTo(F(10).Raw));
                Assert.That(changes[0].NewValue.BaseValue.Raw, Is.EqualTo(F(20).Raw));
                Assert.That(changes[0].NewValue.CurrentValue.Raw, Is.EqualTo(F(50).Raw));
            }
        }

        [Test]
        public void DefaultCurrentReportsPersistentApplyRemoveAndExactContext()
        {
            EffectRuntime runtime = World();
            List<AttributeChange> changes = new List<AttributeChange>();
            runtime.Subscribe(B, X, changes.Add);
            Ok(runtime.TryBeginFrame(0));
            EffectResult applied = runtime.TryApply(B, Spec(7, Constant(5), A));
            Ok(applied);
            Ok(runtime.TryRemove(B, applied.InstanceId));
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(changes[0].ChangedChannels, Is.EqualTo(AttributeChangeChannels.Current));
            Assert.That(changes[0].Cause.Reason, Is.EqualTo(AttributeChangeReason.EffectApplied));
            Assert.That(changes[1].Cause.Reason, Is.EqualTo(AttributeChangeReason.EffectRemoved));
            Assert.That(changes.All(change => change.Cause.InstanceId == applied.InstanceId &&
                change.Cause.DefinitionId == new EffectDefinitionId(7) && change.Cause.SourceId == A &&
                change.Cause.TriggerEntityId == B && change.EntityId == B && change.AttributeId == X &&
                change.Frame == 0 && change.Lifecycle == 1), Is.True);
        }

        [Test]
        public void BothCoalescesWithinTransactionAndDetectsSingleRawUnit()
        {
            EffectRuntime runtime = World();
            List<AttributeChange> changes = new List<AttributeChange>();
            runtime.Subscribe(A, X, changes.Add, AttributeChangeChannels.Both);
            Ok(runtime.TryBeginFrame(0));
            Ok(runtime.TryExecute(A, new[] { SetCommand(20), SetCommand(30) }));
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].OldValue.BaseValue.Raw, Is.EqualTo(F(10).Raw));
            Assert.That(changes[0].NewValue.BaseValue.Raw, Is.EqualTo(F(30).Raw));
            Assert.That(changes[0].ChangedChannels, Is.EqualTo(AttributeChangeChannels.Both));
            Ok(runtime.TryExecute(A, new[]
            {
                new AttributeCommand(AttributeCommandKind.Instant, X, AttributeOperation.Override, FP64.FromRaw(F(30).Raw + 1))
            }));
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(changes[1].NewValue.CurrentValue.Raw - changes[1].OldValue.CurrentValue.Raw, Is.EqualTo(1));
        }

        [Test]
        public void NetZeroTransactionIsSilentButSeparateTransactionsArePreserved()
        {
            EffectRuntime runtime = World();
            List<AttributeChange> changes = new List<AttributeChange>();
            runtime.Subscribe(A, X, changes.Add);
            Ok(runtime.TryBeginFrame(0));
            Ok(runtime.TryExecute(A, new[] { SetCommand(20), SetCommand(10) }));
            Assert.That(changes, Is.Empty);
            Set(runtime, A, 20);
            Set(runtime, A, 10);
            Assert.That(changes.Count, Is.EqualTo(2));
        }

        [Test]
        public void CrossEntityChangesAreSortedAndFullyCommittedBeforeCallbacks()
        {
            EffectRuntime runtime = World();
            Ok(runtime.TryBeginFrame(0));
            Ok(runtime.TryApply(B, Spec(1, Read(EffectValueKind.SourceCurrent), A)));
            Ok(runtime.TryApply(C, Spec(2, Read(EffectValueKind.SourceCurrent), B)));
            List<AttributeChange> changes = new List<AttributeChange>();
            foreach (EntityId id in new[] { C, B, A })
            {
                runtime.Subscribe(id, X, change =>
                {
                    Assert.That(Value(runtime, C).CurrentValue.Raw, Is.EqualTo(F(1120).Raw));
                    changes.Add(change);
                });
            }

            Set(runtime, A, 20);
            Assert.That(changes.Select(change => change.EntityId), Is.EqualTo(new[] { A, B, C }));
            Assert.That(changes.All(change => change.Cause.Reason == AttributeChangeReason.DirectCommand &&
                change.Cause.TriggerEntityId == A && !change.Cause.InstanceId.IsValid), Is.True);
            Assert.That(runtime.LastNotificationException, Is.Null);
        }

        [Test]
        public void ResourceMaximumTruncationReportsBothChannelsInAttributeOrder()
        {
            EffectRuntime runtime = new EffectRuntime();
            Assert.That(AttributeStore.TryCreate(new[]
            {
                new AttributeConfiguration(Y, F(80), F(0), F(1000), AttributeKind.Resource, X),
                new AttributeConfiguration(X, F(100), F(0), F(1000))
            }, out AttributeStore store).Succeeded, Is.True);
            Ok(runtime.TryRegister(A, store, out _));
            List<AttributeChange> changes = new List<AttributeChange>();
            runtime.Subscribe(A, Y, changes.Add);
            runtime.Subscribe(A, X, changes.Add);
            Ok(runtime.TryBeginFrame(0));
            Set(runtime, A, 50);
            Assert.That(changes.Select(change => change.AttributeId), Is.EqualTo(new[] { X, Y }));
            Assert.That(changes[1].OldValue.BaseValue.Raw, Is.EqualTo(F(80).Raw));
            Assert.That(changes[1].NewValue.BaseValue.Raw, Is.EqualTo(F(50).Raw));
            Assert.That(changes[1].ChangedChannels, Is.EqualTo(AttributeChangeChannels.Both));
        }

        [Test]
        public void DownstreamFailurePublishesNothingAndPreservesCompleteState()
        {
            EffectRuntime runtime = World();
            Ok(runtime.TryBeginFrame(0));
            EffectValue expression = EffectValue.Arithmetic(EffectArithmetic.Divide, Constant(10),
                EffectValue.Arithmetic(EffectArithmetic.Subtract, Read(EffectValueKind.SourceBase), Constant(20)));
            Ok(runtime.TryApply(B, Spec(1, expression, A)));
            List<AttributeChange> changes = new List<AttributeChange>();
            runtime.Subscribe(A, X, changes.Add);
            runtime.Subscribe(A, Y, changes.Add);
            runtime.Subscribe(B, X, changes.Add);
            string before = runtime.CaptureState().Hash;
            EffectResult failed = runtime.TryExecute(A, new[] { SetCommand(7, Y), SetCommand(20) });
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(changes, Is.Empty);
            Assert.That(runtime.CaptureState().Hash, Is.EqualTo(before));
            Set(runtime, A, 15);
            Assert.That(changes.Count, Is.EqualTo(2));
        }

        [Test]
        public void ApplyManyPreservesSuccessfulTargetsAndPublishesAfterAllTargets()
        {
            EffectRuntime runtime = World();
            List<AttributeChange> changes = new List<AttributeChange>();
            foreach (EntityId id in new[] { A, B, C })
            {
                runtime.Subscribe(id, X, change =>
                {
                    Assert.That(Value(runtime, B).CurrentValue.Raw, Is.EqualTo(F(110).Raw));
                    Assert.That(Value(runtime, C).CurrentValue.Raw, Is.EqualTo(F(1010).Raw));
                    changes.Add(change);
                });
            }

            Ok(runtime.TryBeginFrame(0));
            IReadOnlyList<EffectResult> results = runtime.ApplyMany(new[] { C, A, B, B },
                Spec(1, Read(EffectValueKind.SourceCurrent), A));
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].Succeeded, Is.False);
            Assert.That(results.Skip(1).All(result => result.Succeeded), Is.True);
            Assert.That(changes.Select(change => change.EntityId), Is.EqualTo(new[] { B, C }));
            Assert.That(runtime.LastNotificationException, Is.Null);
        }

        [Test]
        public void FinitePeriodicEffectNotifiesOnlyAtValidTicksAndKeepsBaseAfterExpiry()
        {
            EffectRuntime runtime = World();
            List<AttributeChange> changes = new List<AttributeChange>();
            runtime.Subscribe(B, X, changes.Add);
            Ok(runtime.TryBeginFrame(0));
            Ok(runtime.TryApply(B, Spec(1, Constant(5), A, EffectDurationKind.Finite, duration: 3, period: 1)));
            Ok(runtime.TryEndFrame());
            Assert.That(changes, Is.Empty);
            for (int frame = 1; frame <= 3; frame++)
            {
                Ok(runtime.TryBeginFrame(frame));
                Ok(runtime.TryEndFrame());
            }

            Assert.That(changes.Select(change => change.Frame), Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(changes.All(change => change.Cause.Reason == AttributeChangeReason.PeriodicExecution), Is.True);
            Assert.That(Value(runtime, B).BaseValue.Raw, Is.EqualTo(F(110).Raw));
        }

        [Test]
        public void ExpiryPublishesAfterEnteringInput()
        {
            EffectRuntime runtime = World();
            Ok(runtime.TryBeginFrame(0));
            Ok(runtime.TryApply(B, Spec(1, Constant(5), A, EffectDurationKind.Finite, duration: 1)));
            Ok(runtime.TryEndFrame());
            List<AttributeChange> changes = new List<AttributeChange>();
            runtime.Subscribe(B, X, change =>
            {
                Assert.That(runtime.Phase, Is.EqualTo(EffectFramePhase.Input));
                changes.Add(change);
            });
            Ok(runtime.TryBeginFrame(1));
            Assert.That(changes.Single().Cause.Reason, Is.EqualTo(AttributeChangeReason.EffectExpired));
            Assert.That(runtime.LastNotificationException, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EarlierSystemCommitStillPublishesWhenLaterTransactionFaults(bool expiry)
        {
            EffectRuntime runtime = World();
            Ok(runtime.TryBeginFrame(0));
            if (expiry)
            {
                Ok(runtime.TryApply(B, Spec(1, Constant(5), A, EffectDurationKind.Finite, duration: 1)));
                Ok(runtime.TryApply(A, Spec(2, Constant(10), A, EffectDurationKind.Finite, duration: 1)));
                Ok(runtime.TryApply(C, Spec(3, EffectValue.Arithmetic(EffectArithmetic.Divide, Constant(10),
                    EffectValue.Arithmetic(EffectArithmetic.Subtract, Read(EffectValueKind.SourceCurrent), Constant(10))), A)));
            }
            else
            {
                Ok(runtime.TryApply(B, Spec(1, Constant(5), A, period: 1)));
                Ok(runtime.TryApply(C, Spec(2, EffectValue.Arithmetic(EffectArithmetic.Divide,
                    Constant(1), Read(EffectValueKind.SourceBase)), A, period: 1)));
            }

            Ok(runtime.TryEndFrame());
            if (!expiry)
            {
                Ok(runtime.TryBeginFrame(1));
                Set(runtime, A, 0);
            }

            List<AttributeChange> changes = new List<AttributeChange>();
            runtime.Subscribe(B, X, change =>
            {
                Assert.That(runtime.Phase, Is.EqualTo(EffectFramePhase.Faulted));
                Assert.That(runtime.LastCompletedFrame, Is.Zero);
                changes.Add(change);
            });
            runtime.Subscribe(C, X, changes.Add);
            EffectResult result = expiry ? runtime.TryBeginFrame(1) : runtime.TryEndFrame();
            Assert.That(result.Succeeded, Is.False);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Cause.Reason, Is.EqualTo(expiry ?
                AttributeChangeReason.EffectExpired : AttributeChangeReason.PeriodicExecution));
            Assert.That(runtime.LastNotificationException, Is.Null);
        }

        [Test]
        public void SubscriptionSurvivesDeactivationRevivalAndFrozenSourceStopsPropagating()
        {
            EffectRuntime runtime = World();
            Ok(runtime.TryBeginFrame(0));
            Ok(runtime.TryApply(A, Spec(1, Constant(5), A)));
            Ok(runtime.TryApply(B, Spec(2, Read(EffectValueKind.SourceCurrent), A)));
            List<AttributeChange> aChanges = new List<AttributeChange>();
            List<AttributeChange> bChanges = new List<AttributeChange>();
            runtime.Subscribe(A, X, aChanges.Add);
            runtime.Subscribe(B, X, bChanges.Add);
            Ok(runtime.TryDeactivate(A));
            Assert.That(aChanges.Single().Cause.Reason, Is.EqualTo(AttributeChangeReason.EntityDeactivated));
            Assert.That(bChanges, Is.Empty);
            Ok(runtime.TryReactivate(A));
            Assert.That(aChanges.Count, Is.EqualTo(1));
            Set(runtime, A, 20);
            Assert.That(aChanges.Count, Is.EqualTo(2));
            Assert.That(aChanges[1].Lifecycle, Is.EqualTo(2));
            Assert.That(bChanges, Is.Empty);
            Assert.That(Value(runtime, B).CurrentValue.Raw, Is.EqualTo(F(115).Raw));
        }

        [Test]
        public void RestoreOnlyResetsAfterSuccessfulCommitAndPreservesSubscriptions()
        {
            EffectRuntime runtime = World();
            EffectRuntimeState saved = runtime.CaptureState();
            List<AttributeChange> changes = new List<AttributeChange>();
            List<RuntimeStateReset> resets = new List<RuntimeStateReset>();
            runtime.Subscribe(A, X, changes.Add);
            runtime.SubscribeStateReset(reset =>
            {
                Assert.That(Value(runtime, A).CurrentValue.Raw, Is.EqualTo(F(10).Raw));
                resets.Add(reset);
            });
            Ok(runtime.TryBeginFrame(0));
            Set(runtime, A, 20);
            changes.Clear();
            Assert.That(runtime.TryRestoreState(null, Array.Empty<EffectDefinition>()).Succeeded, Is.False);
            Assert.That(resets, Is.Empty);
            Ok(runtime.TryRestoreState(saved, Array.Empty<EffectDefinition>()));
            Assert.That(changes, Is.Empty);
            Assert.That(resets.Count, Is.EqualTo(1));
            Assert.That(resets[0].PreviousFrame, Is.Zero);
            Assert.That(resets[0].Frame, Is.EqualTo(-1));
            Assert.That(resets[0].Phase, Is.EqualTo(EffectFramePhase.BetweenFrames));
            Ok(runtime.TryBeginFrame(0));
            Set(runtime, A, 30);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(runtime.LastNotificationException, Is.Null);
        }

        [Test]
        public void ExceptionsAreIsolatedAndEveryMutationEntryRejectsCallbackReentrancy()
        {
            EffectRuntime runtime = World();
            EffectRuntimeState saved = runtime.CaptureState();
            EffectSpec spec = Spec(1, Constant(1), A);
            List<Exception> errors = new List<Exception>();
            List<EffectResult> rejected = new List<EffectResult>();
            runtime.NotificationFailed += errors.Add;
            runtime.NotificationFailed += exception =>
            {
                throw new InvalidOperationException("诊断回调异常");
            };
            runtime.Subscribe(A, X, change =>
            {
                rejected.Add(runtime.TryBeginFrame(1));
                rejected.Add(runtime.TryEndFrame());
                rejected.Add(runtime.TryApply(B, spec));
                rejected.Add(runtime.TryRemove(A, new EffectInstanceId(1)));
                rejected.Add(runtime.TryExecute(A, new[] { SetCommand(99) }));
                rejected.Add(runtime.TryDeactivate(A));
                rejected.Add(runtime.TryReactivate(A));
                rejected.Add(runtime.TryRestoreState(saved, Array.Empty<EffectDefinition>()));
                rejected.Add(runtime.TryRegister(C, null, out _));
                rejected.AddRange(runtime.ApplyMany(new[] { B, C }, spec));
                throw new InvalidOperationException("属性回调异常");
            });
            int delivered = 0;
            runtime.Subscribe(A, X, change => delivered++);
            Ok(runtime.TryBeginFrame(0));
            Set(runtime, A, 20);
            Assert.That(rejected.Count, Is.EqualTo(10));
            Assert.That(rejected.All(result => result.Error == EffectError.NotificationReentrancy), Is.True);
            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(runtime.LastNotificationException.Message, Is.EqualTo("诊断回调异常"));
            Assert.That(runtime.Phase, Is.EqualTo(EffectFramePhase.Input));
            Assert.That(Value(runtime, A).BaseValue.Raw, Is.EqualTo(F(20).Raw));
            Assert.That(runtime.IsPublishingNotifications, Is.False);
        }

        [Test]
        public void ResetCallbacksAlsoRejectWritesAndIsolateExceptions()
        {
            EffectRuntime runtime = World();
            EffectRuntimeState saved = runtime.CaptureState();
            EffectError error = EffectError.None;
            runtime.SubscribeStateReset(reset =>
            {
                error = runtime.TryBeginFrame(0).Error;
                runtime.TryGetController(A, out EffectController controller);
                Assert.That(controller.Attributes.TryExecute(new[] { SetCommand(99) }).Error,
                    Is.EqualTo(AttributeError.StoreOwnedByRuntime));
                throw new InvalidOperationException("重置回调异常");
            });
            int delivered = 0;
            runtime.SubscribeStateReset(reset => delivered++);
            Ok(runtime.TryRestoreState(saved, Array.Empty<EffectDefinition>()));
            Assert.That(error, Is.EqualTo(EffectError.NotificationReentrancy));
            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(runtime.CaptureState().Hash, Is.EqualTo(saved.Hash));
            Assert.That(runtime.LastNotificationException.Message, Is.EqualTo("重置回调异常"));
        }

        [Test]
        public void DisposeSkipsPendingCallbacksAndNewSubscriptionsWaitForNextBatch()
        {
            EffectRuntime runtime = World();
            Ok(runtime.TryBeginFrame(0));
            Ok(runtime.TryApply(B, Spec(1, Read(EffectValueKind.SourceCurrent), A)));
            List<int> order = new List<int>();
            IDisposable removed = null;
            IDisposable self = null;
            IDisposable added = null;
            self = runtime.Subscribe(A, X, change =>
            {
                order.Add(1);
                removed.Dispose();
                removed.Dispose();
                self.Dispose();
                added = runtime.Subscribe(B, X, next => order.Add(3));
            });
            removed = runtime.Subscribe(B, X, change => order.Add(2));
            Set(runtime, A, 20);
            Assert.That(order, Is.EqualTo(new[] { 1 }));
            Set(runtime, A, 30);
            Assert.That(order, Is.EqualTo(new[] { 1, 3 }));
            added.Dispose();
        }

        [Test]
        public void SubscriptionAndExceptionStateNeverEnterCanonicalSimulationState()
        {
            EffectRuntime first = World();
            EffectRuntime second = World();
            first.Subscribe(A, X, change =>
            {
                throw new InvalidOperationException("监听异常");
            });
            Ok(first.TryBeginFrame(0));
            Ok(second.TryBeginFrame(0));
            Set(first, A, 20);
            Set(second, A, 20);
            Assert.That(first.CaptureState().GetCanonicalBytes(), Is.EqualTo(second.CaptureState().GetCanonicalBytes()));
        }

        [Test]
        public void InvalidSubscriptionsAreRejectedAndInitialOrUnchangedValuesAreSilent()
        {
            EffectRuntime runtime = World();
            Action<AttributeChange> ignore = change =>
            {
            };
            Assert.Throws<ArgumentNullException>(() => runtime.Subscribe(A, X, null));
            Assert.Throws<ArgumentNullException>(() => runtime.SubscribeStateReset(null));
            Assert.Throws<ArgumentException>(() => runtime.Subscribe(new EntityId(999), X, ignore));
            Assert.Throws<ArgumentException>(() => runtime.Subscribe(A, new AttributeId(999), ignore));
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Subscribe(A, X, ignore, 0));
            List<AttributeChange> changes = new List<AttributeChange>();
            runtime.Subscribe(A, X, changes.Add);
            Assert.That(changes, Is.Empty);
            Ok(runtime.TryBeginFrame(0));
            Set(runtime, A, 10);
            Assert.That(changes, Is.Empty);
        }

        private static EffectRuntime World()
        {
            EffectRuntime runtime = new EffectRuntime();
            foreach (EntityId id in new[] { C, A, B })
            {
                int initial = id == A ? 10 : id == B ? 100 : 1000;
                Assert.That(AttributeStore.TryCreate(new[]
                {
                    new AttributeConfiguration(X, F(initial), F(-1000000000), F(1000000000)),
                    new AttributeConfiguration(Y, F(0), F(-1000000000), F(1000000000))
                }, out AttributeStore store).Succeeded, Is.True);
                Ok(runtime.TryRegister(id, store, out _));
            }

            return runtime;
        }

        private static EffectSpec Spec(uint id, EffectValue value, EntityId source,
            EffectDurationKind kind = EffectDurationKind.Infinite, long duration = 0, long period = 0,
            AttributeOperation operation = AttributeOperation.Add)
        {
            Ok(EffectDefinition.TryCreate(new EffectDefinitionId(id), kind, duration, period,
                new[] { new EffectModifier(X, operation, value) }, out EffectDefinition definition));
            Ok(EffectSpec.TryCreate(definition, source, Array.Empty<EffectParameter>(), out EffectSpec spec));
            return spec;
        }

        private static AttributeCommand SetCommand(int value, AttributeId attribute = default)
        {
            return new AttributeCommand(AttributeCommandKind.Instant, attribute.IsValid ? attribute : X,
                AttributeOperation.Override, F(value));
        }

        private static void Set(EffectRuntime runtime, EntityId id, int value)
        {
            Ok(runtime.TryExecute(id, new[] { SetCommand(value) }));
        }

        private static AttributeValue Value(EffectRuntime runtime, EntityId id)
        {
            Assert.That(runtime.TryGetController(id, out EffectController controller), Is.True);
            Assert.That(controller.Attributes.TryGetValue(X, out AttributeValue value), Is.True);
            return value;
        }

        private static EffectValue Read(EffectValueKind kind) => new EffectValue(kind, attributeId: X);
        private static EffectValue Constant(int value) => new EffectValue(EffectValueKind.Constant, F(value));
        private static FP64 F(int value) => FP64.FromInt(value);

        private static void Ok(EffectResult result)
        {
            Assert.That(result.Succeeded, Is.True, $"{result.Error}/{result.AttributeResult.Error}");
        }
    }
}
