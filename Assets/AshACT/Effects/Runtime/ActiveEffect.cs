using System;
using System.Collections.Generic;
using AshACT.Attributes;

namespace AshACT.Effects
{
    /// <summary>活跃状态的不可变视图；后续周期以新视图替换，旧视图仍保持原值。</summary>
    public sealed class ActiveEffect
    {
        public EffectInstanceId Id { get; }
        public EffectSpec Spec { get; }
        public EntityId TargetId { get; }
        public long StartFrame { get; }
        public long? EndFrame { get; }
        public long? NextPeriodFrame { get; }
        public IReadOnlyList<ulong> ModifierHandles { get; }

        internal AttributeCommand[] Commands { get; }

        internal ActiveEffect(EffectInstanceId id, EffectSpec spec, EntityId targetId,
            long startFrame, long? endFrame, long? nextPeriodFrame, AttributeCommand[] commands)
        {
            Id = id;
            Spec = spec;
            TargetId = targetId;
            StartFrame = startFrame;
            EndFrame = endFrame;
            NextPeriodFrame = nextPeriodFrame;
            Commands = commands;
            ulong[] handles = spec.Definition.PeriodFrames == 0 ? new ulong[commands.Length] : Array.Empty<ulong>();
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = commands[i].ModifierHandle;
            }

            ModifierHandles = Array.AsReadOnly(handles);
        }
    }
}
