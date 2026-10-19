using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using AshACT.Attributes;

namespace AshACT.Effects
{
    public sealed class EffectEntityState
    {
        public EntityId EntityId { get; }
        public bool IsActive { get; }
        public ulong Lifecycle { get; }
        public AttributeStoreState Attributes { get; }
        public IReadOnlyList<ActiveEffect> Effects { get; }

        internal EffectEntityState(EffectController controller)
        {
            EntityId = controller.EntityId;
            IsActive = controller.IsActive;
            Lifecycle = controller.Lifecycle;
            Attributes = controller.Attributes.CaptureState();
            Effects = controller.GetActiveEffects();
        }
    }

    /// <summary>首期进程内只读恢复协议；规范字节用于完整等价比较和 SHA256 校验。</summary>
    public sealed class EffectRuntimeState
    {
        public const int ProtocolVersion = 1;
        public long CurrentFrame { get; }
        public long LastCompletedFrame { get; }
        public ulong LastInstanceId { get; }
        public ulong LastModifierHandle { get; }
        public EffectFramePhase Phase { get; }
        public EffectResult Fault { get; }
        public IReadOnlyList<EffectEntityState> Entities { get; }
        public string Hash { get; }

        internal EffectRuntimeState(EffectRuntime runtime, EffectEntityState[] entities)
        {
            CurrentFrame = runtime.CurrentFrame;
            LastCompletedFrame = runtime.LastCompletedFrame;
            LastInstanceId = runtime.LastInstanceId;
            LastModifierHandle = runtime.LastModifierHandle;
            Phase = runtime.Phase;
            Fault = runtime.Fault;
            Entities = Array.AsReadOnly(entities);
            using (SHA256 algorithm = SHA256.Create())
            {
                Hash = BitConverter.ToString(algorithm.ComputeHash(GetCanonicalBytes())).Replace("-", "");
            }
        }

        public byte[] GetCanonicalBytes()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(ProtocolVersion);
                    writer.Write(CurrentFrame);
                    writer.Write(LastCompletedFrame);
                    writer.Write(LastInstanceId);
                    writer.Write(LastModifierHandle);
                    writer.Write((int)Phase);
                    writer.Write((int)Fault.Error);
                    writer.Write(Fault.TargetId.Value);
                    writer.Write(Fault.InstanceId.Value);
                    writer.Write(Fault.ModifierIndex);
                    writer.Write(Fault.ParameterId);
                    writer.Write(Fault.AttributeId.Value);
                    writer.Write((int)Fault.AttributeResult.Error);
                    writer.Write(Fault.AttributeResult.AttributeId.Value);
                    writer.Write(Fault.AttributeResult.CommandIndex);
                    writer.Write(Entities.Count);
                    foreach (EffectEntityState entity in Entities)
                    {
                        writer.Write(entity.EntityId.Value);
                        writer.Write(entity.IsActive);
                        writer.Write(entity.Lifecycle);
                        WriteStore(writer, entity.Attributes);
                        writer.Write(entity.Effects.Count);
                        foreach (ActiveEffect effect in entity.Effects)
                        {
                            writer.Write(effect.Id.Value);
                            WriteDefinition(writer, effect.Spec.Definition);
                            writer.Write(effect.Spec.SourceId.Value);
                            SortedDictionary<uint, Framework.FixedPoint.FP64> parameters =
                                new SortedDictionary<uint, Framework.FixedPoint.FP64>();
                            foreach (KeyValuePair<uint, Framework.FixedPoint.FP64> parameter in effect.Spec.Parameters)
                            {
                                parameters.Add(parameter.Key, parameter.Value);
                            }

                            writer.Write(parameters.Count);
                            foreach (KeyValuePair<uint, Framework.FixedPoint.FP64> parameter in parameters)
                            {
                                writer.Write(parameter.Key);
                                writer.Write(parameter.Value.Raw);
                            }

                            writer.Write(effect.TargetId.Value);
                            writer.Write(effect.StartFrame);
                            WriteFrame(writer, effect.EndFrame);
                            WriteFrame(writer, effect.NextPeriodFrame);
                            writer.Write(effect.SourceLifecycle);
                            writer.Write(effect.IsSourceFrozen);
                            if (effect.IsSourceFrozen)
                            {
                                writer.Write(effect.FrozenSource.EntityId.Value);
                                writer.Write(effect.FrozenSource.Lifecycle);
                                WriteStore(writer, effect.FrozenSource.Attributes);
                            }

                            writer.Write(effect.Commands.Length);
                            foreach (AttributeCommand command in effect.Commands)
                            {
                                WriteCommand(writer, command);
                            }
                        }
                    }

                    writer.Flush();
                    return stream.ToArray();
                }
            }
        }

        private static void WriteFrame(BinaryWriter writer, long? frame)
        {
            writer.Write(frame.HasValue);
            if (frame.HasValue)
            {
                writer.Write(frame.Value);
            }
        }

        internal static void WriteStore(BinaryWriter writer, AttributeStoreState store)
        {
            writer.Write(store.LastApplicationSequence);
            writer.Write(store.Entries.Count);
            foreach (AttributeEntryState entry in store.Entries)
            {
                AttributeConfiguration configuration = entry.Configuration;
                writer.Write(configuration.Id.Value);
                writer.Write((int)configuration.Kind);
                writer.Write(configuration.InitialBaseValue.Raw);
                writer.Write(configuration.Minimum.Raw);
                writer.Write(configuration.Maximum.Raw);
                writer.Write(configuration.MaximumAttribute.Value);
                writer.Write(entry.Value.BaseValue.Raw);
                writer.Write(entry.Value.CurrentValue.Raw);
                writer.Write(entry.Modifiers.Count);
                foreach (AttributeModifierState modifier in entry.Modifiers)
                {
                    WriteCommand(writer, modifier.Command);
                    writer.Write(modifier.Sequence);
                }
            }
        }

        internal static void WriteCommand(BinaryWriter writer, AttributeCommand command)
        {
            writer.Write((int)command.Kind);
            writer.Write(command.AttributeId.Value);
            writer.Write((int)command.Operation);
            writer.Write(command.Operand.Raw);
            writer.Write(command.ModifierHandle);
            writer.Write(command.Priority);
        }

        internal static void WriteDefinition(BinaryWriter writer, EffectDefinition definition)
        {
            writer.Write(definition.Id.Value);
            writer.Write((int)definition.DurationKind);
            writer.Write(definition.DurationFrames);
            writer.Write(definition.PeriodFrames);
            writer.Write(definition.Markers.Count);
            foreach (MarkerId marker in definition.Markers)
            {
                writer.Write(marker.Value);
            }

            writer.Write(definition.Modifiers.Count);
            foreach (EffectModifier modifier in definition.Modifiers)
            {
                writer.Write(modifier.AttributeId.Value);
                writer.Write((int)modifier.Operation);
                writer.Write(modifier.Priority);
                WriteValue(writer, modifier.Value);
            }
        }

        private static void WriteValue(BinaryWriter writer, EffectValue value)
        {
            writer.Write((int)value.Kind);
            writer.Write(value.Constant.Raw);
            writer.Write(value.ParameterId);
            writer.Write(value.AttributeId.Value);
            if (value.Kind == EffectValueKind.Arithmetic)
            {
                writer.Write((int)value.Expression.Operation);
                WriteValue(writer, value.Expression.Left);
                WriteValue(writer, value.Expression.Right);
            }
        }

        internal static string DefinitionIdentity(EffectDefinition definition)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    WriteDefinition(writer, definition);
                    writer.Flush();
                    return Convert.ToBase64String(stream.ToArray());
                }
            }
        }
    }

    public sealed partial class EffectRuntime
    {
        public EffectRuntimeState CaptureState()
        {
            EffectEntityState[] entities = new EffectEntityState[m_Controllers.Count];
            int index = 0;
            foreach (EffectController controller in m_Controllers.Values)
            {
                entities[index] = new EffectEntityState(controller);
                index++;
            }

            return new EffectRuntimeState(this, entities);
        }

        /// <summary>调用方先注册相同实体和属性配置，再提供锁定的效果定义清单。失败不改变当前域。</summary>
        public EffectResult TryRestoreState(EffectRuntimeState state, IReadOnlyList<EffectDefinition> definitions)
        {
            if (state == null || definitions == null || !ValidFrameState(state))
            {
                return new EffectResult(EffectError.InvalidState);
            }

            Dictionary<EffectDefinitionId, EffectDefinition> catalog = new Dictionary<EffectDefinitionId, EffectDefinition>();
            foreach (EffectDefinition definition in definitions)
            {
                if (definition == null || catalog.ContainsKey(definition.Id))
                {
                    return new EffectResult(EffectError.ConfigurationMismatch);
                }

                catalog.Add(definition.Id, definition);
            }

            EffectRuntime candidate = ForkDomain();
            if (state.Entities.Count != candidate.m_Controllers.Count)
            {
                return new EffectResult(EffectError.ConfigurationMismatch);
            }

            candidate.CurrentFrame = state.CurrentFrame;
            candidate.LastCompletedFrame = state.LastCompletedFrame;
            candidate.LastInstanceId = state.LastInstanceId;
            candidate.LastModifierHandle = state.LastModifierHandle;
            candidate.Phase = state.Phase;
            candidate.Fault = state.Fault;
            HashSet<EffectInstanceId> instances = new HashSet<EffectInstanceId>();
            HashSet<EntityId> entities = new HashSet<EntityId>();
            foreach (EffectEntityState entity in state.Entities)
            {
                if (!entities.Add(entity.EntityId) ||
                    !candidate.m_Controllers.TryGetValue(entity.EntityId, out EffectController existing) ||
                    entity.Lifecycle == 0 || !existing.Attributes.LoadState(entity.Attributes))
                {
                    return new EffectResult(EffectError.ConfigurationMismatch, entity.EntityId);
                }

                EffectController controller = new EffectController(entity.EntityId, existing.Attributes)
                {
                    IsActive = entity.IsActive,
                    Lifecycle = entity.Lifecycle
                };
                candidate.m_Controllers[entity.EntityId] = controller;
                HashSet<ulong> ownedHandles = new HashSet<ulong>();
                foreach (ActiveEffect saved in entity.Effects)
                {
                    if (!entity.IsActive || !saved.Id.IsValid || saved.Id.Value > state.LastInstanceId ||
                        !instances.Add(saved.Id) || saved.TargetId != entity.EntityId ||
                        !catalog.TryGetValue(saved.Spec.Definition.Id, out EffectDefinition definition) ||
                        EffectRuntimeState.DefinitionIdentity(definition) != EffectRuntimeState.DefinitionIdentity(saved.Spec.Definition))
                    {
                        return new EffectResult(EffectError.ConfigurationMismatch, entity.EntityId, saved.Id);
                    }

                    if (!ValidSchedule(saved, state) || !ValidCommands(saved, entity.Attributes, ownedHandles, state.LastModifierHandle))
                    {
                        return new EffectResult(EffectError.InvalidState, entity.EntityId, saved.Id);
                    }

                    List<EffectParameter> parameters = new List<EffectParameter>();
                    foreach (KeyValuePair<uint, Framework.FixedPoint.FP64> parameter in saved.Spec.Parameters)
                    {
                        parameters.Add(new EffectParameter(parameter.Key, parameter.Value));
                    }

                    EffectResult specResult = EffectSpec.TryCreate(definition, saved.Spec.SourceId, parameters, out EffectSpec spec);
                    if (!specResult.Succeeded)
                    {
                        return specResult;
                    }

                    controller.Commit(new ActiveEffect(saved.Id, spec, saved.TargetId, saved.StartFrame, saved.EndFrame,
                        saved.NextPeriodFrame, saved.Commands, saved.SourceLifecycle, saved.FrozenSource));
                }
            }

            foreach (ActiveEffect effect in candidate.OrderedEffects())
            {
                if (!candidate.ValidBinding(effect))
                {
                    return new EffectResult(EffectError.InvalidState, effect.TargetId, effect.Id);
                }
            }

            EffectResult calculation = candidate.Recalculate();
            if (!calculation.Succeeded)
            {
                return calculation;
            }

            // 重建图和重算只能验证状态，不能悄悄修复错误 Raw、操作数或顺序。
            if (candidate.CaptureState().Hash != state.Hash)
            {
                return new EffectResult(EffectError.InvalidState);
            }

            CommitDomain(candidate);
            return new EffectResult(EffectError.None);
        }

        private bool ValidBinding(ActiveEffect effect)
        {
            if (effect.IsSourceFrozen)
            {
                SourceSnapshot frozen = effect.FrozenSource;
                if (frozen.EntityId != effect.Spec.SourceId || frozen.Lifecycle != effect.SourceLifecycle ||
                    frozen.Lifecycle == 0 || !m_Controllers.TryGetValue(frozen.EntityId, out EffectController source) ||
                    frozen.Lifecycle > source.Lifecycle || (source.IsActive && frozen.Lifecycle == source.Lifecycle))
                {
                    return false;
                }

                AttributeStore validation = source.Attributes.Fork();
                if (!validation.LoadState(frozen.Attributes))
                {
                    return false;
                }

                AttributeStoreState before = validation.CaptureState();
                if (!validation.TryExecute(Array.Empty<AttributeCommand>()).Succeeded)
                {
                    return false;
                }

                foreach (AttributeId id in validation.AttributeIds)
                {
                    if (!validation.RecalculateNode(id, false).Succeeded || !validation.RecalculateNode(id, true).Succeeded)
                    {
                        return false;
                    }
                }

                return SameStore(before, validation.CaptureState());
            }

            if (m_Controllers.TryGetValue(effect.Spec.SourceId, out EffectController live))
            {
                return live.IsActive && effect.SourceLifecycle == live.Lifecycle;
            }

            if (effect.SourceLifecycle != 0)
            {
                return false;
            }

            foreach (EffectModifier modifier in effect.Spec.Definition.Modifiers)
            {
                foreach (EffectValue leaf in modifier.Value.Leaves())
                {
                    if (leaf.Kind == EffectValueKind.SourceBase || leaf.Kind == EffectValueKind.SourceCurrent)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SameStore(AttributeStoreState left, AttributeStoreState right)
        {
            using (MemoryStream a = new MemoryStream())
            {
                using (MemoryStream b = new MemoryStream())
                {
                    using (BinaryWriter wa = new BinaryWriter(a))
                    {
                        using (BinaryWriter wb = new BinaryWriter(b))
                        {
                            EffectRuntimeState.WriteStore(wa, left);
                            EffectRuntimeState.WriteStore(wb, right);
                            wa.Flush();
                            wb.Flush();
                            return Convert.ToBase64String(a.ToArray()) == Convert.ToBase64String(b.ToArray());
                        }
                    }
                }
            }
        }

        private static bool ValidFrameState(EffectRuntimeState state)
        {
            if (state.CurrentFrame < -1 || state.LastCompletedFrame < -1 ||
                state.Phase < EffectFramePhase.BetweenFrames || state.Phase > EffectFramePhase.Faulted)
            {
                return false;
            }

            if (state.Phase == EffectFramePhase.BetweenFrames)
            {
                return state.CurrentFrame == state.LastCompletedFrame && state.Fault.Succeeded;
            }

            return state.LastCompletedFrame < long.MaxValue && state.CurrentFrame == state.LastCompletedFrame + 1 &&
                (state.Phase == EffectFramePhase.Faulted ? !state.Fault.Succeeded : state.Fault.Succeeded);
        }

        private static bool ValidSchedule(ActiveEffect effect, EffectRuntimeState state)
        {
            EffectDefinition definition = effect.Spec.Definition;
            if (effect.StartFrame < 0 || effect.StartFrame > state.CurrentFrame ||
                definition.DurationKind == EffectDurationKind.Instant ||
                (definition.DurationKind == EffectDurationKind.Finite &&
                    (effect.StartFrame > long.MaxValue - definition.DurationFrames ||
                    effect.EndFrame != effect.StartFrame + definition.DurationFrames)) ||
                (definition.DurationKind == EffectDurationKind.Infinite && effect.EndFrame.HasValue))
            {
                return false;
            }

            if (effect.EndFrame.HasValue && effect.EndFrame <= state.CurrentFrame && state.Phase != EffectFramePhase.Faulted)
            {
                return false;
            }

            if (effect.NextPeriodFrame.HasValue)
            {
                long next = effect.NextPeriodFrame.Value;
                if (definition.PeriodFrames <= 0 || next <= effect.StartFrame || next < state.CurrentFrame ||
                    (next == state.CurrentFrame && state.Phase == EffectFramePhase.BetweenFrames) ||
                    (next - effect.StartFrame) % definition.PeriodFrames != 0 ||
                    (effect.EndFrame.HasValue && next >= effect.EndFrame.Value))
                {
                    return false;
                }
            }
            else if (definition.PeriodFrames > 0)
            {
                // 无计时器只允许有限区间或整数帧域确实没有后续触发。
                long anchor = state.CurrentFrame < effect.StartFrame ? effect.StartFrame : state.CurrentFrame;
                long remainder = (anchor - effect.StartFrame) % definition.PeriodFrames;
                long delta = definition.PeriodFrames - remainder;
                if (anchor <= long.MaxValue - delta &&
                    (!effect.EndFrame.HasValue || anchor + delta < effect.EndFrame.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidCommands(ActiveEffect effect, AttributeStoreState store,
            HashSet<ulong> ownedHandles, ulong lastHandle)
        {
            if (effect.Commands.Length != effect.Spec.Definition.Modifiers.Count)
            {
                return false;
            }

            bool persistent = effect.Spec.Definition.PeriodFrames == 0;
            for (int i = 0; i < effect.Commands.Length; i++)
            {
                AttributeCommand command = effect.Commands[i];
                EffectModifier modifier = effect.Spec.Definition.Modifiers[i];
                if (command.AttributeId != modifier.AttributeId || command.Operation != modifier.Operation ||
                    command.Priority != modifier.Priority || command.Kind !=
                    (persistent ? AttributeCommandKind.AddPersistent : AttributeCommandKind.Instant))
                {
                    return false;
                }

                if (!persistent)
                {
                    if (command.ModifierHandle != 0)
                    {
                        return false;
                    }

                    continue;
                }

                if (command.ModifierHandle == 0 || command.ModifierHandle > lastHandle || !ownedHandles.Add(command.ModifierHandle))
                {
                    return false;
                }

                bool found = false;
                foreach (AttributeEntryState entry in store.Entries)
                {
                    foreach (AttributeModifierState saved in entry.Modifiers)
                    {
                        if (saved.Command.Equals(command))
                        {
                            found = true;
                        }
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
