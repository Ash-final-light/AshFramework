using System.Collections.Generic;
using Framework.FixedPoint;

namespace AshACT.Attributes
{
    internal readonly struct PersistentModifier
    {
        internal AttributeCommand Command { get; }
        internal ulong Sequence { get; }

        internal PersistentModifier(AttributeCommand command, ulong sequence)
        {
            Command = command;
            Sequence = sequence;
        }
    }

    internal static class AttributeAggregator
    {
        internal static AttributeError Evaluate(FP64 baseValue, List<PersistentModifier> modifiers, out FP64 value)
        {
            value = default;
            FP64 additive = FP64.Zero;
            FP64 multiplier = FP64.One;
            FP64 divisor = FP64.One;
            PersistentModifier? winner = null;
            foreach (PersistentModifier modifier in modifiers)
            {
                AttributeCommand command = modifier.Command;
                AttributeError error = AttributeError.None;
                switch (command.Operation)
                {
                    case AttributeOperation.Add:
                        error = AttributeMath.Add(additive, command.Operand, out additive);
                        break;
                    case AttributeOperation.Multiply:
                        error = AttributeMath.Subtract(command.Operand, FP64.One, out FP64 multipleDelta);
                        if (error == AttributeError.None)
                        {
                            error = AttributeMath.Add(multiplier, multipleDelta, out multiplier);
                        }
                        break;
                    case AttributeOperation.Divide:
                        error = AttributeMath.Subtract(command.Operand, FP64.One, out FP64 divisorDelta);
                        if (error == AttributeError.None)
                        {
                            error = AttributeMath.Add(divisor, divisorDelta, out divisor);
                        }
                        break;
                    case AttributeOperation.Override:
                        if (!winner.HasValue || command.Priority > winner.Value.Command.Priority ||
                            (command.Priority == winner.Value.Command.Priority && modifier.Sequence > winner.Value.Sequence))
                        {
                            winner = modifier;
                        }
                        break;
                    default:
                        return AttributeError.InvalidOperation;
                }

                if (error != AttributeError.None)
                {
                    return error;
                }
            }

            if (winner.HasValue)
            {
                value = winner.Value.Command.Operand;
                return AttributeError.None;
            }

            if (multiplier < FP64.Zero)
            {
                multiplier = FP64.Zero;
            }

            AttributeError result = AttributeMath.Add(baseValue, additive, out FP64 sum);
            if (result != AttributeError.None)
            {
                return result;
            }

            result = AttributeMath.Multiply(sum, multiplier, out FP64 product);
            if (result != AttributeError.None)
            {
                return result;
            }

            return AttributeMath.Divide(product, divisor, out value);
        }
    }
}
