using System;
using System.Collections.Generic;
using AshACT.Attributes;
using Framework.FixedPoint;
using NUnit.Framework;

namespace AshACT.Effects.Tests
{
    public sealed class EffectConfigurationTests
    {
        [Test]
        public void DefinitionSpecAndPublicViewsDefensivelyCopyTheirInputs()
        {
            MarkerId[] markers = { new MarkerId(1) };
            EffectModifier[] modifiers =
            {
                new EffectModifier(new AttributeId(1), AttributeOperation.Add,
                    new EffectValue(EffectValueKind.SetByCaller, parameterId: 7))
            };
            EffectParameter[] parameters = { new EffectParameter(7, FP64.FromInt(10)) };
            Assert.That(EffectDefinition.TryCreate(new EffectDefinitionId(1), EffectDurationKind.Infinite,
                0, 0, markers, modifiers, out EffectDefinition definition).Succeeded, Is.True);
            Assert.That(EffectSpec.TryCreate(definition, new EntityId(1), parameters, out EffectSpec spec).Succeeded, Is.True);
            markers[0] = new MarkerId(2);
            modifiers[0] = default;
            parameters[0] = new EffectParameter(7, FP64.FromInt(99));
            Assert.That(definition.Markers[0], Is.EqualTo(new MarkerId(1)));
            Assert.That(definition.Modifiers[0].Value.ParameterId, Is.EqualTo(7));
            Assert.That(spec.Parameters[7].Raw, Is.EqualTo(FP64.FromInt(10).Raw));
            Assert.Throws<NotSupportedException>(() => ((IList<MarkerId>)definition.Markers)[0] = new MarkerId(3));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<uint, FP64>)spec.Parameters)[7] = FP64.Zero);
        }

        [TestCase(EffectDurationKind.Instant, 1, 0)]
        [TestCase(EffectDurationKind.Instant, 0, 1)]
        [TestCase(EffectDurationKind.Finite, 0, 0)]
        [TestCase(EffectDurationKind.Finite, -1, 0)]
        [TestCase(EffectDurationKind.Infinite, 1, 0)]
        [TestCase(EffectDurationKind.Infinite, 0, -1)]
        [TestCase((EffectDurationKind)99, 0, 0)]
        public void InvalidTimeConfigurationReturnsStructuredFailure(EffectDurationKind kind, long duration, long period)
        {
            EffectResult result = EffectDefinition.TryCreate(new EffectDefinitionId(1), kind, duration, period,
                Array.Empty<MarkerId>(), Array.Empty<EffectModifier>(), out EffectDefinition definition);
            Assert.That(result.Error, Is.EqualTo(EffectError.InvalidDefinition));
            Assert.That(definition, Is.Null);
        }

        [TestCase(EffectValueKind.SourceBase)]
        [TestCase(EffectValueKind.SourceCurrent)]
        [TestCase(EffectValueKind.TargetBase)]
        [TestCase(EffectValueKind.TargetCurrent)]
        [TestCase(EffectValueKind.Arithmetic)]
        [TestCase((EffectValueKind)99)]
        public void UnsupportedNodesAreExplicitlyRejected(EffectValueKind kind)
        {
            EffectResult result = EffectDefinition.TryCreate(new EffectDefinitionId(1), EffectDurationKind.Infinite, 0, 0,
                Array.Empty<MarkerId>(), new[]
                {
                    new EffectModifier(new AttributeId(1), AttributeOperation.Add, new EffectValue(kind))
                }, out EffectDefinition definition);
            Assert.That(result.Error, Is.EqualTo(EffectError.UnsupportedValueSource));
            Assert.That(result.ModifierIndex, Is.Zero);
            Assert.That(definition, Is.Null);
        }

        [Test]
        public void InvalidIdsDuplicateMarkersAndNullListsDoNotProduceDefinitions()
        {
            Assert.That(Create(default, Array.Empty<MarkerId>(), Array.Empty<EffectModifier>()).Error,
                Is.EqualTo(EffectError.InvalidDefinition));
            Assert.That(Create(new EffectDefinitionId(1), null, Array.Empty<EffectModifier>()).Error,
                Is.EqualTo(EffectError.InvalidDefinition));
            Assert.That(Create(new EffectDefinitionId(1), Array.Empty<MarkerId>(), null).Error,
                Is.EqualTo(EffectError.InvalidDefinition));
            Assert.That(Create(new EffectDefinitionId(1), new[] { new MarkerId(0) }, Array.Empty<EffectModifier>()).Error,
                Is.EqualTo(EffectError.InvalidDefinition));
            Assert.That(Create(new EffectDefinitionId(1), new[] { new MarkerId(1), new MarkerId(1) }, Array.Empty<EffectModifier>()).Error,
                Is.EqualTo(EffectError.InvalidDefinition));
            Assert.That(Create(new EffectDefinitionId(1), Array.Empty<MarkerId>(), new[]
            {
                new EffectModifier(new AttributeId(1), AttributeOperation.Add, new EffectValue(EffectValueKind.SetByCaller))
            }).Error, Is.EqualTo(EffectError.InvalidDefinition));
            Assert.That(Create(new EffectDefinitionId(1), Array.Empty<MarkerId>(), new[]
            {
                new EffectModifier(default, AttributeOperation.Add, new EffectValue(EffectValueKind.Constant))
            }).Error, Is.EqualTo(EffectError.InvalidDefinition));
        }

        [TestCase(AttributeOperation.Multiply, -1)]
        [TestCase(AttributeOperation.Divide, 0)]
        [TestCase(AttributeOperation.Divide, -1)]
        [TestCase((AttributeOperation)99, 1)]
        public void InvalidConstantOperationOrOperandIsRejected(AttributeOperation operation, int value)
        {
            EffectResult result = Create(new EffectDefinitionId(1), Array.Empty<MarkerId>(), new[]
            {
                new EffectModifier(new AttributeId(1), operation,
                    new EffectValue(EffectValueKind.Constant, FP64.FromInt(value)))
            });
            Assert.That(result.Error, Is.EqualTo(EffectError.InvalidDefinition));
            Assert.That(result.ModifierIndex, Is.Zero);
        }

        [Test]
        public void SpecRejectsInvalidDuplicateOrOutOfRangeParameters()
        {
            Assert.That(EffectDefinition.TryCreate(new EffectDefinitionId(1), EffectDurationKind.Infinite, 0, 0,
                Array.Empty<MarkerId>(), Array.Empty<EffectModifier>(), out EffectDefinition definition).Succeeded, Is.True);
            Assert.That(EffectSpec.TryCreate(null, new EntityId(1), Array.Empty<EffectParameter>(), out _).Error,
                Is.EqualTo(EffectError.InvalidSpec));
            Assert.That(EffectSpec.TryCreate(definition, default, Array.Empty<EffectParameter>(), out _).Error,
                Is.EqualTo(EffectError.InvalidSpec));
            Assert.That(EffectSpec.TryCreate(definition, new EntityId(1), null, out _).Error,
                Is.EqualTo(EffectError.InvalidSpec));
            Assert.That(EffectSpec.TryCreate(definition, new EntityId(1), new[] { new EffectParameter(0, FP64.One) }, out _).Error,
                Is.EqualTo(EffectError.InvalidParameter));
            Assert.That(EffectSpec.TryCreate(definition, new EntityId(1), new[] { new EffectParameter(1, FP64.MaxValue) }, out _).Error,
                Is.EqualTo(EffectError.InvalidParameter));
            EffectResult duplicate = EffectSpec.TryCreate(definition, new EntityId(1), new[]
            {
                new EffectParameter(1, FP64.Zero),
                new EffectParameter(1, FP64.One)
            }, out _);
            Assert.That(duplicate.Error, Is.EqualTo(EffectError.DuplicateParameter));
            Assert.That(duplicate.ParameterId, Is.EqualTo(1));
        }

        private static EffectResult Create(EffectDefinitionId id, MarkerId[] markers, EffectModifier[] modifiers)
        {
            EffectResult result = EffectDefinition.TryCreate(id, EffectDurationKind.Infinite, 0, 0,
                markers, modifiers, out EffectDefinition definition);
            Assert.That(definition, Is.Null);
            return result;
        }
    }
}
