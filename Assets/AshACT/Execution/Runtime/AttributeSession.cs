using System;
using System.Collections.Generic;
using AshACT.Attributes;
using AshACT.Effects;

namespace AshACT.Unity
{
    /// <summary>一个战斗域持有一个会话；显式整数帧驱动，不读取 Unity 时间。</summary>
    public sealed class AttributeSession
    {
        public CompiledSessionConfiguration Configuration { get; }
        public EffectRuntime Runtime { get; }
        public EffectResult LastResult { get; private set; }

        public AttributeSession(CompiledSessionConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Runtime = new EffectRuntime();
            List<ulong> entities = new List<ulong>(configuration.Entities.Keys);
            entities.Sort();
            foreach (ulong id in entities)
            {
                AttributeResult creation = AttributeStore.TryCreate(configuration.Profiles[configuration.Entities[id]].Attributes,
                    out AttributeStore store);
                if (!creation.Succeeded)
                {
                    throw new InvalidOperationException(creation.Error.ToString());
                }

                EffectResult registration = Runtime.TryRegister(new EntityId(id), store, out EffectController controller);
                if (!registration.Succeeded)
                {
                    throw new InvalidOperationException(registration.Error.ToString());
                }
            }
        }

        public EffectResult BeginFrame(long frame)
        {
            LastResult = Runtime.TryBeginFrame(frame);
            return LastResult;
        }

        /// <summary>订阅本会话属性变化；更换会话时应释放旧令牌并重新绑定。</summary>
        public IDisposable Subscribe(ulong entityId, uint attributeId, Action<AttributeChange> callback,
            AttributeChangeChannels channels = AttributeChangeChannels.Current)
        {
            return Runtime.Subscribe(new EntityId(entityId), new AttributeId(attributeId), callback, channels);
        }

        public IDisposable SubscribeStateReset(Action<RuntimeStateReset> callback)
        {
            return Runtime.SubscribeStateReset(callback);
        }

        public EffectResult EndFrame()
        {
            LastResult = Runtime.TryEndFrame();
            return LastResult;
        }

        public bool TryApply(uint definitionId, ulong source, ulong target, IReadOnlyList<EffectParameter> parameters,
            out string error)
        {
            error = null;
            if (!Configuration.Effects.TryGetValue(definitionId, out CompiledEffect effect))
            {
                error = "效果编号不在本会话锁定的配置中。";
                return false;
            }

            EffectResult result = EffectSpec.TryCreate(effect.Definition, new EntityId(source), parameters, out EffectSpec spec);
            LastResult = result;
            if (result.Succeeded)
            {
                foreach (uint id in effect.RequiredParameters)
                {
                    if (!spec.Parameters.ContainsKey(id))
                    {
                        error = $"缺少必填参数 {id}。";
                        return false;
                    }
                }

                LastResult = Runtime.TryApply(new EntityId(target), spec);
            }

            if (!LastResult.Succeeded)
            {
                error = Describe(LastResult);
            }

            return LastResult.Succeeded;
        }

        public EffectResult Deactivate(ulong entityId)
        {
            LastResult = Runtime.TryDeactivate(new EntityId(entityId));
            return LastResult;
        }

        public EffectResult Reactivate(ulong entityId)
        {
            LastResult = Runtime.TryReactivate(new EntityId(entityId));
            return LastResult;
        }

        public EffectResult Execute(ulong entityId, IReadOnlyList<AttributeCommand> commands)
        {
            LastResult = Runtime.TryExecute(new EntityId(entityId), commands);
            return LastResult;
        }

        public static string Describe(EffectResult result)
        {
            return $"{result.Error}: Entity={result.TargetId}, Instance={result.InstanceId}, Modifier={result.ModifierIndex}, " +
                $"Parameter={result.ParameterId}, Attribute={result.AttributeId}, Store={result.AttributeResult.Error}";
        }

    }
}
