using System;
using AshACT.Attributes;
using AshACT.Effects;
using UnityEngine;

namespace AshACT.Unity
{
    [Serializable]
    public sealed class EffectValueNode
    {
        public int NodeId;
        public EffectValueKind Kind;
        public string Constant = "0.0000";
        public uint ParameterId;
        public uint AttributeId;
        public EffectArithmetic Arithmetic;
        public int LeftNodeId;
        public int RightNodeId;
        public Vector2 Position;
    }

    [Serializable]
    public sealed class EffectModifierNode
    {
        public uint AttributeId;
        public AttributeOperation Operation;
        public int Priority;
        public int ValueNodeId;
        public Vector2 Position;
    }

    [CreateAssetMenu(menuName = "AshACT/效果节点图")]
    public sealed class EffectGraphAsset : VersionedConfigurationAsset
    {
        public uint EffectDefinitionId;
        public EffectDurationKind DurationKind;
        public long DurationFrames;
        public long PeriodFrames;
        public uint[] MarkerIds = Array.Empty<uint>();
        public uint[] RequiredParameters = Array.Empty<uint>();
        public EffectValueNode[] Nodes = Array.Empty<EffectValueNode>();
        public EffectModifierNode[] Modifiers = Array.Empty<EffectModifierNode>();
    }
}
