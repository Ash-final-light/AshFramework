using System;
using System.Collections.Generic;
using AshACT.Attributes;
using Framework.FixedPoint;

namespace AshACT.Effects
{
    public sealed partial class EffectRuntime
    {
        public EffectResult TryGetDependencyOrder(out IReadOnlyList<DependencyNode> order)
        {
            EffectResult result = BuildOrder(out List<DependencyNode> nodes);
            order = nodes.AsReadOnly();
            return result;
        }

        private EffectResult BuildOrder(out List<DependencyNode> order)
        {
            DependencyGraph graph = new DependencyGraph();
            foreach (EffectController controller in m_Controllers.Values)
            {
                foreach (AttributeId id in controller.Attributes.AttributeIds)
                {
                    DependencyNode baseNode = new DependencyNode(controller.EntityId, id, AttributeReadKind.Base);
                    DependencyNode currentNode = new DependencyNode(controller.EntityId, id, AttributeReadKind.Current);
                    graph.AddEdge(baseNode, currentNode);
                    AttributeConfiguration configuration = controller.Attributes.Configuration(id);
                    if (configuration.MaximumAttribute.IsValid)
                    {
                        graph.AddEdge(new DependencyNode(controller.EntityId, configuration.MaximumAttribute,
                            AttributeReadKind.Current), baseNode);
                    }
                }
            }

            foreach (ActiveEffect effect in OrderedEffects())
            {
                if (effect.Spec.Definition.PeriodFrames != 0)
                {
                    continue;
                }

                foreach (EffectModifier modifier in effect.Spec.Definition.Modifiers)
                {
                    DependencyNode destination = new DependencyNode(effect.TargetId, modifier.AttributeId, AttributeReadKind.Current);
                    foreach (EffectValue leaf in modifier.Value.Leaves())
                    {
                        bool source = leaf.Kind == EffectValueKind.SourceBase || leaf.Kind == EffectValueKind.SourceCurrent;
                        bool target = leaf.Kind == EffectValueKind.TargetBase || leaf.Kind == EffectValueKind.TargetCurrent;
                        if ((!source && !target) || (source && effect.IsSourceFrozen))
                        {
                            continue;
                        }

                        EntityId entity = source ? effect.Spec.SourceId : effect.TargetId;
                        if (!m_Controllers.TryGetValue(entity, out EffectController controller) ||
                            !controller.Attributes.TryGetValue(leaf.AttributeId, out _))
                        {
                            order = new List<DependencyNode>();
                            return new EffectResult(source ? EffectError.MissingSource : EffectError.MissingAttribute,
                                effect.TargetId, effect.Id, attributeId: leaf.AttributeId);
                        }

                        bool current = leaf.Kind == EffectValueKind.SourceCurrent || leaf.Kind == EffectValueKind.TargetCurrent;
                        graph.AddEdge(new DependencyNode(entity, leaf.AttributeId,
                            current ? AttributeReadKind.Current : AttributeReadKind.Base), destination);
                    }
                }
            }

            return new EffectResult(graph.TryOrder(out order) ? EffectError.None : EffectError.DependencyCycle);
        }

        private EffectResult Recalculate(bool allowPendingModifiers = false)
        {
            EffectResult graph = BuildOrder(out List<DependencyNode> order);
            if (!graph.Succeeded)
            {
                return graph;
            }

            foreach (DependencyNode node in order)
            {
                EffectController controller = m_Controllers[node.EntityId];
                if (node.Kind == AttributeReadKind.Current)
                {
                    foreach (ActiveEffect view in controller.GetActiveEffects())
                    {
                        if (view.Spec.Definition.PeriodFrames != 0)
                        {
                            continue;
                        }

                        AttributeCommand[] commands = (AttributeCommand[])view.Commands.Clone();
                        for (int i = 0; i < commands.Length; i++)
                        {
                            AttributeCommand command = commands[i];
                            if (command.AttributeId != node.AttributeId)
                            {
                                continue;
                            }

                            if (allowPendingModifiers && !controller.Attributes.HasModifier(command.ModifierHandle))
                            {
                                continue;
                            }

                            EffectResult evaluated = Evaluate(view.Spec.Definition.Modifiers[i].Value, view.Spec,
                                view.TargetId, view.FrozenSource, out FP64 operand);
                            if (!evaluated.Succeeded)
                            {
                                return AtModifier(evaluated, view.TargetId, view.Id, i);
                            }

                            commands[i] = new AttributeCommand(command.Kind, command.AttributeId,
                                command.Operation, operand, command.ModifierHandle, command.Priority);
                            AttributeResult replaced = controller.Attributes.ReplaceOperand(commands[i]);
                            if (!replaced.Succeeded)
                            {
                                return AttributeFailure(view.TargetId, view.Id,
                                    new AttributeResult(replaced.Error, replaced.AttributeId, i));
                            }
                        }

                        controller.Commit(new ActiveEffect(view.Id, view.Spec, view.TargetId, view.StartFrame,
                            view.EndFrame, view.NextPeriodFrame, commands, view.SourceLifecycle, view.FrozenSource));
                    }
                }

                AttributeResult result = controller.Attributes.RecalculateNode(node.AttributeId,
                    node.Kind == AttributeReadKind.Current);
                if (!result.Succeeded)
                {
                    return AttributeFailure(node.EntityId, default, result);
                }
            }

            return new EffectResult(EffectError.None);
        }

        private EffectResult Evaluate(EffectValue expression, EffectSpec spec, EntityId target,
            SourceSnapshot frozen, out FP64 value)
        {
            value = default;
            switch (expression.Kind)
            {
                case EffectValueKind.Constant:
                    value = expression.Constant;
                    return new EffectResult(EffectError.None);
                case EffectValueKind.SetByCaller:
                    return new EffectResult(spec.Parameters.TryGetValue(expression.ParameterId, out value) ?
                        EffectError.None : EffectError.MissingParameter, target, parameterId: expression.ParameterId);
                case EffectValueKind.SourceBase:
                case EffectValueKind.SourceCurrent:
                case EffectValueKind.TargetBase:
                case EffectValueKind.TargetCurrent:
                    bool source = expression.Kind == EffectValueKind.SourceBase || expression.Kind == EffectValueKind.SourceCurrent;
                    bool current = expression.Kind == EffectValueKind.SourceCurrent || expression.Kind == EffectValueKind.TargetCurrent;
                    if (source && frozen != null)
                    {
                        return new EffectResult(frozen.TryRead(expression.AttributeId, current, out value) ?
                            EffectError.None : EffectError.MissingAttribute, target, attributeId: expression.AttributeId);
                    }

                    if (!m_Controllers.TryGetValue(source ? spec.SourceId : target, out EffectController controller))
                    {
                        return new EffectResult(source ? EffectError.MissingSource : EffectError.MissingTarget, target,
                            attributeId: expression.AttributeId);
                    }

                    if (!controller.IsActive)
                    {
                        return new EffectResult(EffectError.EntityInactive, target, attributeId: expression.AttributeId);
                    }

                    if (!controller.Attributes.TryGetValue(expression.AttributeId, out AttributeValue read))
                    {
                        return new EffectResult(EffectError.MissingAttribute, target, attributeId: expression.AttributeId);
                    }

                    value = current ? read.CurrentValue : read.BaseValue;
                    return new EffectResult(EffectError.None);
                case EffectValueKind.Arithmetic:
                    EffectResult left = Evaluate(expression.Expression.Left, spec, target, frozen, out FP64 a);
                    if (!left.Succeeded)
                    {
                        return left;
                    }

                    EffectResult right = Evaluate(expression.Expression.Right, spec, target, frozen, out FP64 b);
                    if (!right.Succeeded)
                    {
                        return right;
                    }

                    AttributeError error;
                    switch (expression.Expression.Operation)
                    {
                        case EffectArithmetic.Add:
                            error = AttributeMath.Add(a, b, out value);
                            break;
                        case EffectArithmetic.Subtract:
                            error = AttributeMath.Subtract(a, b, out value);
                            break;
                        case EffectArithmetic.Multiply:
                            error = AttributeMath.Multiply(a, b, out value);
                            break;
                        case EffectArithmetic.Divide:
                            error = AttributeMath.Divide(a, b, out value);
                            break;
                        default:
                            return new EffectResult(EffectError.InvalidDefinition, target);
                    }

                    return error == AttributeError.None ? new EffectResult(EffectError.None) :
                        AttributeFailure(target, default, new AttributeResult(error));
                default:
                    return new EffectResult(EffectError.UnsupportedValueSource, target);
            }
        }
    }
}
