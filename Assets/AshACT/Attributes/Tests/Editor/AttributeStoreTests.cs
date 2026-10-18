using Framework.FixedPoint;
using NUnit.Framework;

namespace AshACT.Attributes.Tests
{
    public sealed class AttributeStoreTests
    {
        private static readonly AttributeId First = new AttributeId(1);
        private static readonly AttributeId Second = new AttributeId(2);
        private static readonly FP64 HalfMultiplier = FP64.FromRaw(FP64.OneRaw + FP64.HalfRaw);

        [Test]
        public void InstantMultipliersCompoundWhilePersistentMultipliersAddTheirBiases()
        {
            AttributeStore instant = Create(Config(First, 100));
            AttributeStore persistent = Create(Config(First, 100));

            Success(instant.TryExecute(new[]
            {
                Instant(First, AttributeOperation.Multiply, HalfMultiplier),
                Instant(First, AttributeOperation.Multiply, HalfMultiplier)
            }));
            Success(persistent.TryExecute(new[]
            {
                Persistent(First, AttributeOperation.Multiply, HalfMultiplier, 1),
                Persistent(First, AttributeOperation.Multiply, HalfMultiplier, 2)
            }));

            Value(instant, First, 225, 225);
            Value(persistent, First, 100, 200);
        }

        [Test]
        public void PersistentDivisorsAddTheirBiases()
        {
            AttributeStore store = Create(Config(First, 120));
            Success(store.TryExecute(new[]
            {
                Persistent(First, AttributeOperation.Divide, FP64.FromInt(2), 1),
                Persistent(First, AttributeOperation.Divide, FP64.FromInt(2), 2)
            }));

            Value(store, First, 120, 40);
        }

        [Test]
        public void AggregationUsesAddThenMultiplyThenDivideRegardlessOfInsertionOrder()
        {
            AttributeStore store = Create(Config(First, 100));
            Success(store.TryExecute(new[]
            {
                Persistent(First, AttributeOperation.Divide, FP64.FromInt(2), 1),
                Persistent(First, AttributeOperation.Multiply, HalfMultiplier, 2),
                Persistent(First, AttributeOperation.Add, FP64.FromInt(10), 3)
            }));

            Assert.That(store.TryGetValue(First, out AttributeValue value), Is.True);
            Assert.That(value.CurrentValue.Raw, Is.EqualTo(82 * FP64.OneRaw + FP64.HalfRaw));
        }

        [Test]
        public void NegativeAggregatedMultiplierIsClampedToZero()
        {
            AttributeStore store = Create(Config(First, 100));
            FP64 quarter = FP64.FromRaw(FP64.OneRaw / 4);
            Success(store.TryExecute(new[]
            {
                Persistent(First, AttributeOperation.Multiply, quarter, 1),
                Persistent(First, AttributeOperation.Multiply, quarter, 2)
            }));

            Value(store, First, 100, 0);
            Success(store.TryExecute(new[] { Remove(First, 2) }));
            Value(store, First, 100, 25);
        }

        [Test]
        public void OverrideSelectsHighestPriorityThenLastApplicationAndRemovalRevealsRemainder()
        {
            AttributeStore store = Create(Config(First, 100));
            Success(store.TryExecute(new[]
            {
                Persistent(First, AttributeOperation.Add, FP64.FromInt(20), 1),
                Persistent(First, AttributeOperation.Override, FP64.FromInt(30), 2, 5),
                Persistent(First, AttributeOperation.Override, FP64.FromInt(40), 3, 5),
                Persistent(First, AttributeOperation.Override, FP64.FromInt(50), 4, 6),
                Persistent(First, AttributeOperation.Override, FP64.FromInt(60), 5, 4)
            }));
            Value(store, First, 100, 50);
            Success(store.TryExecute(new[] { Remove(First, 4) }));
            Value(store, First, 100, 40);
            Success(store.TryExecute(new[] { Remove(First, 3) }));
            Value(store, First, 100, 30);
            Success(store.TryExecute(new[] { Remove(First, 2), Remove(First, 5) }));
            Value(store, First, 100, 120);
        }

        [Test]
        public void RemovalRecomputesFromLatestBaseRatherThanReversingHistory()
        {
            AttributeStore store = Create(Config(First, 100));
            Success(store.TryExecute(new[]
            {
                Persistent(First, AttributeOperation.Multiply, HalfMultiplier, 1),
                Persistent(First, AttributeOperation.Multiply, HalfMultiplier, 2),
                Instant(First, AttributeOperation.Add, FP64.FromInt(20))
            }));
            Value(store, First, 120, 240);
            Success(store.TryExecute(new[] { Remove(First, 1) }));
            Value(store, First, 120, 180);
            Success(store.TryExecute(new[] { Remove(First, 2) }));
            Value(store, First, 120, 120);
        }

        [Test]
        public void StatCurrentClampingDoesNotOverwriteBase()
        {
            AttributeStore store = Create(Config(First, 90, 0, 100));
            Success(store.TryExecute(new[] { Persistent(First, AttributeOperation.Add, FP64.FromInt(50), 1) }));
            Value(store, First, 90, 100);
            Success(store.TryExecute(new[] { Remove(First, 1) }));
            Value(store, First, 90, 90);
        }

        [Test]
        public void InstantCommandsRespectConfigurationOrderAndClampAfterEachOperation()
        {
            AttributeStore store = Create(Config(First, 90, 0, 100));
            Success(store.TryExecute(new[]
            {
                Instant(First, AttributeOperation.Add, FP64.FromInt(50)),
                Instant(First, AttributeOperation.Divide, FP64.FromInt(2))
            }));
            Value(store, First, 50, 50);
            Success(store.TryExecute(new[]
            {
                Instant(First, AttributeOperation.Override, FP64.FromInt(20)),
                Instant(First, AttributeOperation.Add, FP64.FromInt(-30))
            }));
            Value(store, First, 0, 0);
        }

        [Test]
        public void ResourceCapReductionPermanentlyClampsBaseAndRestoringCapDoesNotRefill()
        {
            AttributeStore store = Create(
                new AttributeConfiguration(First, FP64.FromInt(90), FP64.Zero, FP64.FromInt(200), AttributeKind.Resource, Second),
                Config(Second, 100, 0, 200));
            Success(store.TryExecute(new[] { Persistent(Second, AttributeOperation.Add, FP64.FromInt(-50), 1) }));
            Value(store, First, 50, 50);
            Success(store.TryExecute(new[] { Remove(Second, 1) }));
            Value(store, First, 50, 50);
            Value(store, Second, 100, 100);
            Success(store.TryExecute(new[] { Instant(First, AttributeOperation.Add, FP64.FromInt(100)) }));
            Value(store, First, 100, 100);
        }

        [Test]
        public void ResourceCapIsSettledBetweenCommandsWithinOneTransaction()
        {
            AttributeStore store = Create(
                new AttributeConfiguration(First, FP64.FromInt(90), FP64.Zero, FP64.FromInt(200), AttributeKind.Resource, Second),
                Config(Second, 100, 0, 200));
            Success(store.TryExecute(new[]
            {
                Instant(Second, AttributeOperation.Override, FP64.FromInt(40)),
                Instant(Second, AttributeOperation.Override, FP64.FromInt(100))
            }));
            Value(store, First, 40, 40);
        }

        [Test]
        public void ResourceRejectsPersistentModifiersAndRollsBackEarlierInstantCommands()
        {
            AttributeStore store = Create(new AttributeConfiguration(First, FP64.FromInt(50), FP64.Zero,
                FP64.FromInt(100), AttributeKind.Resource));
            AttributeResult result = store.TryExecute(new[]
            {
                Instant(First, AttributeOperation.Add, FP64.FromInt(10)),
                Persistent(First, AttributeOperation.Multiply, HalfMultiplier, 1)
            });

            Assert.That(result.Error, Is.EqualTo(AttributeError.ResourceModifierNotAllowed));
            Value(store, First, 50, 50);
            Assert.That(store.HasModifier(1), Is.False);
            Assert.That(store.LastApplicationSequence, Is.Zero);
        }

        [Test]
        public void OverflowRollsBackAllValuesAdditionsRemovalsAndApplicationSequence()
        {
            AttributeStore store = Create(Config(First, 10), Config(Second, 1000000000));
            Success(store.TryExecute(new[] { Persistent(First, AttributeOperation.Add, FP64.FromInt(5), 10) }));
            AttributeValue before = Read(store, First);
            AttributeResult result = store.TryExecute(new[]
            {
                Remove(First, 10),
                Persistent(First, AttributeOperation.Add, FP64.FromInt(8), 20),
                Instant(First, AttributeOperation.Add, FP64.FromInt(5)),
                Instant(Second, AttributeOperation.Multiply, FP64.FromInt(1000000000))
            });

            Assert.That(result.Error, Is.EqualTo(AttributeError.Overflow));
            Assert.That(result.AttributeId, Is.EqualTo(Second));
            Assert.That(result.CommandIndex, Is.EqualTo(3));
            Assert.That(store.HasModifier(10), Is.True);
            Assert.That(store.HasModifier(20), Is.False);
            Assert.That(store.LastApplicationSequence, Is.EqualTo(1UL));
            Assert.That(Read(store, First).BaseValue.Raw, Is.EqualTo(before.BaseValue.Raw));
            Assert.That(Read(store, First).CurrentValue.Raw, Is.EqualTo(before.CurrentValue.Raw));
            Value(store, Second, 1000000000, 1000000000);
            Success(store.TryExecute(new[] { Persistent(First, AttributeOperation.Add, FP64.One, 20) }));
            Assert.That(store.LastApplicationSequence, Is.EqualTo(2UL));
        }

        [Test]
        public void AggregateOverflowCannotBeHiddenByFinalDivisionOrClamping()
        {
            AttributeStore store = Create(Config(First, 1000000000));
            Success(store.TryExecute(new[] { Persistent(First, AttributeOperation.Divide, FP64.FromInt(3), 1) }));
            long before = Read(store, First).CurrentValue.Raw;
            AttributeResult result = store.TryExecute(new[] { Persistent(First, AttributeOperation.Multiply, FP64.FromInt(3), 2) });

            Assert.That(result.Error, Is.EqualTo(AttributeError.Overflow));
            Assert.That(Read(store, First).CurrentValue.Raw, Is.EqualTo(before));
            Assert.That(store.HasModifier(2), Is.False);
        }

        [Test]
        public void FailedRemovalKeepsOverrideAndAllOtherModifiersIntact()
        {
            AttributeStore store = Create(Config(First, 1000000000));
            Success(store.TryExecute(new[]
            {
                Persistent(First, AttributeOperation.Override, FP64.FromInt(42), 1),
                Persistent(First, AttributeOperation.Multiply, FP64.FromInt(1000000000), 2)
            }));
            Value(store, First, 1000000000, 42);

            AttributeResult result = store.TryExecute(new[] { Remove(First, 1) });

            Assert.That(result.Error, Is.EqualTo(AttributeError.Overflow));
            Assert.That(store.HasModifier(1), Is.True);
            Assert.That(store.HasModifier(2), Is.True);
            Assert.That(store.LastApplicationSequence, Is.EqualTo(2UL));
            Value(store, First, 1000000000, 42);
        }

        [Test]
        public void AdditiveAccumulatorOverflowRollsBackWholeBatch()
        {
            AttributeStore store = Create(Config(First, 0));
            AttributeResult result = store.TryExecute(new[]
            {
                Persistent(First, AttributeOperation.Add, FP64.FromInt(1000000000), 1),
                Persistent(First, AttributeOperation.Add, FP64.FromInt(1000000000), 2),
                Persistent(First, AttributeOperation.Add, FP64.FromInt(1000000000), 3)
            });

            Assert.That(result.Error, Is.EqualTo(AttributeError.Overflow));
            Value(store, First, 0, 0);
            Assert.That(store.LastApplicationSequence, Is.Zero);
            Assert.That(store.HasModifier(1), Is.False);
        }

        [Test]
        public void SharedConfigurationAndCommandArraysDoNotShareCharacterState()
        {
            AttributeConfiguration[] configurations = { Config(First, 100) };
            AttributeStore first = Create(configurations);
            AttributeStore second = Create(configurations);
            AttributeCommand[] commands = { Persistent(First, AttributeOperation.Add, FP64.FromInt(10), 1) };
            Success(first.TryExecute(commands));
            configurations[0] = Config(First, 999);
            commands[0] = Instant(First, AttributeOperation.Override, FP64.Zero);

            Value(first, First, 100, 110);
            Value(second, First, 100, 100);
            Assert.That(second.HasModifier(1), Is.False);
            Success(second.TryExecute(new[] { Persistent(First, AttributeOperation.Add, FP64.FromInt(20), 1) }));
            Success(first.TryExecute(new[] { Remove(First, 1) }));
            Value(first, First, 100, 100);
            Value(second, First, 100, 120);
        }

        [TestCase(AttributeCommandKind.Instant)]
        [TestCase(AttributeCommandKind.AddPersistent)]
        public void DivideByZeroRollsBackEarlierCommands(AttributeCommandKind kind)
        {
            AttributeStore store = Create(Config(First, 100));
            AttributeResult result = store.TryExecute(new[]
            {
                Instant(First, AttributeOperation.Add, FP64.FromInt(10)),
                new AttributeCommand(kind, First, AttributeOperation.Divide, FP64.Zero, 1)
            });

            Assert.That(result.Error, Is.EqualTo(AttributeError.DivideByZero));
            Value(store, First, 100, 100);
        }

        [TestCase(AttributeOperation.Multiply, -1)]
        [TestCase(AttributeOperation.Divide, -1)]
        public void InvalidMultipliersAndDivisorsAreRejected(AttributeOperation operation, int operand)
        {
            AttributeStore store = Create(Config(First, 100));
            AttributeResult result = store.TryExecute(new[] { Persistent(First, operation, FP64.FromInt(operand), 1) });
            Assert.That(result.Error, Is.EqualTo(AttributeError.InvalidOperand));
            Value(store, First, 100, 100);
        }

        [Test]
        public void MissingAttributeAndDuplicateHandleCannotPartiallyApply()
        {
            AttributeStore store = Create(Config(First, 100), Config(Second, 100));
            AttributeResult missing = store.TryExecute(new[]
            {
                Instant(First, AttributeOperation.Add, FP64.One),
                Instant(new AttributeId(99), AttributeOperation.Add, FP64.One)
            });
            Assert.That(missing.Error, Is.EqualTo(AttributeError.MissingAttribute));
            Value(store, First, 100, 100);
            AttributeResult duplicate = store.TryExecute(new[]
            {
                Persistent(First, AttributeOperation.Add, FP64.One, 1),
                Persistent(Second, AttributeOperation.Add, FP64.One, 1)
            });
            Assert.That(duplicate.Error, Is.EqualTo(AttributeError.DuplicateModifier));
            Assert.That(store.HasModifier(1), Is.False);
        }

        [Test]
        public void RemovalMustMatchAttributeAndNonzeroHandle()
        {
            AttributeStore store = Create(Config(First, 100), Config(Second, 100));
            Success(store.TryExecute(new[] { Persistent(First, AttributeOperation.Add, FP64.One, 1) }));
            Assert.That(store.TryExecute(new[] { Remove(Second, 1) }).Error, Is.EqualTo(AttributeError.MissingModifier));
            Assert.That(store.TryExecute(new[] { Remove(First, 0) }).Error, Is.EqualTo(AttributeError.InvalidModifierHandle));
            Assert.That(store.HasModifier(1), Is.True);
        }

        [Test]
        public void InvalidConfigurationNeverPublishesAStore()
        {
            AttributeResult zero = AttributeStore.TryCreate(new[] { Config(default, 1) }, out AttributeStore store);
            Assert.That(zero.Error, Is.EqualTo(AttributeError.InvalidAttributeId));
            Assert.That(store, Is.Null);
            AttributeResult duplicate = AttributeStore.TryCreate(new[] { Config(First, 1), Config(First, 2) }, out store);
            Assert.That(duplicate.Error, Is.EqualTo(AttributeError.DuplicateAttribute));
            Assert.That(store, Is.Null);
            AttributeResult bounds = AttributeStore.TryCreate(new[] { Config(First, 1, 100, 0) }, out store);
            Assert.That(bounds.Error, Is.EqualTo(AttributeError.InvalidConfiguration));
            Assert.That(store, Is.Null);
        }

        [Test]
        public void ResourceMaximumMustExistAndBeAStat()
        {
            AttributeConfiguration resource = new AttributeConfiguration(First, FP64.One, FP64.Zero,
                FP64.FromInt(100), AttributeKind.Resource, Second);
            Assert.That(AttributeStore.TryCreate(new[] { resource }, out _).Error, Is.EqualTo(AttributeError.MissingAttribute));
            AttributeConfiguration otherResource = new AttributeConfiguration(Second, FP64.One, FP64.Zero,
                FP64.FromInt(100), AttributeKind.Resource);
            Assert.That(AttributeStore.TryCreate(new[] { resource, otherResource }, out _).Error,
                Is.EqualTo(AttributeError.InvalidConfiguration));
        }

        [Test]
        public void InvalidDynamicResourceRangeRollsBackStatAndResourceValues()
        {
            AttributeStore store = Create(
                new AttributeConfiguration(First, FP64.FromInt(50), FP64.FromInt(10), FP64.FromInt(100), AttributeKind.Resource, Second),
                Config(Second, 100, -100, 100));
            AttributeResult result = store.TryExecute(new[] { Instant(Second, AttributeOperation.Override, FP64.Zero) });
            Assert.That(result.Error, Is.EqualTo(AttributeError.InvalidConfiguration));
            Value(store, First, 50, 50);
            Value(store, Second, 100, 100);
        }

        [Test]
        public void ReplayingSameCommandsProducesIdenticalRawValuesAndSequences()
        {
            AttributeCommand[] commands =
            {
                Persistent(First, AttributeOperation.Multiply, HalfMultiplier, 7),
                Instant(First, AttributeOperation.Add, FP64.FromRaw(17)),
                Persistent(First, AttributeOperation.Divide, FP64.FromInt(3), 8),
                Remove(First, 7)
            };
            long expected = 0;
            for (int replay = 0; replay < 20; replay++)
            {
                AttributeStore store = Create(Config(Second, 3), Config(First, 100));
                Success(store.TryExecute(commands));
                long raw = Read(store, First).CurrentValue.Raw;
                if (replay == 0)
                {
                    expected = raw;
                }

                Assert.That(raw, Is.EqualTo(expected));
                Assert.That(raw, Is.EqualTo((100 * FP64.OneRaw + 17) / 3));
                Assert.That(store.LastApplicationSequence, Is.EqualTo(2UL));
                Assert.That(store.AttributeIds[0], Is.EqualTo(First));
            }
        }

        private static AttributeConfiguration Config(AttributeId id, int initial, int minimum = -1000000000, int maximum = 1000000000)
        {
            return new AttributeConfiguration(id, FP64.FromInt(initial), FP64.FromInt(minimum), FP64.FromInt(maximum));
        }

        private static AttributeStore Create(params AttributeConfiguration[] configurations)
        {
            Success(AttributeStore.TryCreate(configurations, out AttributeStore store));
            return store;
        }

        private static AttributeCommand Instant(AttributeId id, AttributeOperation operation, FP64 operand)
        {
            return new AttributeCommand(AttributeCommandKind.Instant, id, operation, operand);
        }

        private static AttributeCommand Persistent(AttributeId id, AttributeOperation operation, FP64 operand, ulong handle, int priority = 0)
        {
            return new AttributeCommand(AttributeCommandKind.AddPersistent, id, operation, operand, handle, priority);
        }

        private static AttributeCommand Remove(AttributeId id, ulong handle)
        {
            return new AttributeCommand(AttributeCommandKind.RemovePersistent, id, modifierHandle: handle);
        }

        private static AttributeValue Read(AttributeStore store, AttributeId id)
        {
            Assert.That(store.TryGetValue(id, out AttributeValue value), Is.True);
            return value;
        }

        private static void Value(AttributeStore store, AttributeId id, int expectedBase, int expectedCurrent)
        {
            AttributeValue value = Read(store, id);
            Assert.That(value.BaseValue.Raw, Is.EqualTo(FP64.FromInt(expectedBase).Raw));
            Assert.That(value.CurrentValue.Raw, Is.EqualTo(FP64.FromInt(expectedCurrent).Raw));
        }

        private static void Success(AttributeResult result)
        {
            Assert.That(result.Succeeded, Is.True,
                $"{result.Error}, attribute {result.AttributeId}, command {result.CommandIndex}");
        }
    }
}
