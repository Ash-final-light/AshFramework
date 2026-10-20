# 节点图方案（伪代码）

本文只描述图的编排层：信号如何传播、节点如何激活、效果如何施加、监听与条件如何接入现有运行时。属性数值、效果时长、周期触发和事务提交仍由 `AshACT.Attributes` 与 `AshACT.Effects` 负责，节点图不重复实现这些规则。伪代码按项目 Allman 风格书写，实际实现按同一规则落地。

## 1. 定位

节点图回答四个问题：

1. 谁：触发信号来自哪个实体。
2. 何时：在哪个逻辑帧、由哪个上游信号触发。
3. 对谁：本次激活作用于哪些 `EntityId`。
4. 做什么：施加哪个效果、发送哪个外部信号、做哪组条件判断。

帧由外部传入的整数驱动，`GraphRuntime.Tick(long frame)` 不读 Unity 时间，不依赖 MonoBehaviour 生命周期，也不要求外部实现任何接口；外部只要按顺序调用即可。

## 2. 现状核对

- 运行时已有 `AttributeSession`、`EffectRuntime`、`EffectDefinition`（时长、周期、修改器）、`EffectSpec`（SetByCaller 参数）、`EffectRuntimeState`（快照恢复）以及 `AttributeChange` 通知。
- 效果内部编辑器已存在：`EffectGraphAsset` 保存时长、周期、修改器、必填参数和标记操作草稿，节点图只需要引用 `EffectDefinitionId`，不要把这些字段再抄到外部节点。
- 全仓没有 `ICharacter` 接口（已检索，无匹配）。现有目标身份只有 `EntityId`（`ulong`），角色到实体的映射属于外部战斗系统。
- 标记（tag）目前只有作者期校验和 `IMarkerRequestHandler` 契约，运行时未接入：`ConfigurationCompiler` 遇到 `MarkerOperations` 会返回 `MarkerRuntimeUnavailable`。
- 通知发布发生在写操作最外层事务结束时，发布期间禁止写入（`EffectError.NotificationReentrancy`，同时 `AttributeStore` 切到受管写入）。监听器回调里不能直接施加效果，只能入队。

## 3. 图数据模型

```csharp
public enum NodeKind
{
    Effect,
    ValueInput,
    AttributeListener,
    Condition,
    Signal
}

[Serializable]
public sealed class GraphNodeData
{
    public int NodeId;                          // 正数、图内唯一
    public NodeKind Kind;
    public Vector2 Position;                    // 仅编辑用
    public uint EffectDefinitionId;             // Effect 节点
    public uint SignalId;                       // Signal 节点
    public TargetPolicyData Targets;            // Effect / Listener / Condition 共用
    public ParameterBindingData[] Parameters;   // ValueInput 节点
    public ListenSettingsData Listen;           // AttributeListener 节点
    public ConditionData Condition;             // Condition 节点
}

[Serializable]
public sealed class GraphEdgeData
{
    public int FromNodeId;
    public int FromPort;   // 0 = 真/主输出，1 = 假输出
    public int ToNodeId;
    public int ToPort;     // 固定 0：强制信号输入槽
}
```

端口只有信号一种。基类节点的输入槽固定一个，可接多条入边，按 OR 合并；输出按节点类型为 1 个或 2 个。

`TargetPolicyData` 只保存策略，不保存具体对象，因为 Unity 无法序列化接口引用，把 `ICharacter` 存进资产还会把图绑到具体场景：

```csharp
public enum TargetPolicyKind
{
    TriggerEntity,   // 取触发信号携带的实体
    ExternalList,    // 每次激活由外部传入实体列表
    AllRegistered    // 会话内全部已注册实体
}
```

## 4. 信号与激活

```csharp
public enum SignalKind
{
    Start,
    Terminate
}

public readonly struct GraphSignal
{
    public SignalKind Kind { get; }
    public int SourceNodeId { get; }
    public ulong SourceActivationId { get; }
    public ulong TriggerEntityId { get; }
    public long Frame { get; }
}

public sealed class Activation
{
    public ulong Id { get; }
    public int NodeId { get; }
    public ulong TriggerEntityId { get; }
    public long Frame { get; }
    public bool Cancelled { get; set; }
    public List<AppliedEffect> Applied { get; } = new List<AppliedEffect>();
}

public readonly struct AppliedEffect
{
    public EntityId Target { get; }
    public EffectInstanceId InstanceId { get; }
}
```

投递规则：`Start` 创建一条新激活记录并交给节点执行；`Terminate` 取消本节点当前全部激活，对应"只要有一个上游发送终止就可以"：

```csharp
private void Deliver(GraphSignal signal)
{
    NodeState node = m_Nodes[signal.TargetNodeId];
    if (signal.Kind == SignalKind.Start)
    {
        Activation activation = new Activation(NextActivationId(), node.NodeId,
            signal.TriggerEntityId, m_Frame);
        node.Activations.Add(activation);
        node.Behavior.OnStart(activation);
        return;
    }

    foreach (Activation activation in node.Activations.ToArray())
    {
        activation.Cancelled = true;
        node.Behavior.OnTerminate(activation);
        node.Activations.Remove(activation);
    }
}
```

激活记录必须独立保存：激活期间再收到 `Start` 不覆盖旧记录；中断只清理对应记录，否则后施加的效果会把先施加的效果一并删掉。

## 5. 帧时序

```csharp
public void Tick(long frame)
{
    m_Frame = frame;

    // 1. 消费上一帧通知阶段入队的事件
    DrainPendingSignals();

    // 2. 处理外部投递的 Start / Terminate
    DrainExternalSignals();

    // 3. 条件节点只读求值
    EvaluateConditions();

    // 4. 图内传播，节点按 NodeId 升序稳定推进
    PropagateSignals();
}
```

外部调用顺序固定：

```csharp
session.BeginFrame(frame);
graph.Tick(frame);
session.EndFrame();
```

`Effect` 节点的写入窗口只能是 `EffectPhase.Input`（`EffectRuntime.RequireInput`），所以 `Tick` 必须落在 `BeginFrame` 与 `EndFrame` 之间；`EndFrame` 发布通知时监听器产生的信号只能入队，下一帧 `DrainPendingSignals` 再消费。

## 6. 节点伪代码

### 6.1 Effect

```csharp
public override void OnStart(Activation activation)
{
    if (!TryResolveTargets(activation, m_Node.Targets, out IReadOnlyList<EntityId> targets))
    {
        Fail(activation, "无法解析作用目标");
        return;
    }

    if (!TryBuildParameters(m_Node, out IReadOnlyList<EffectParameter> parameters))
    {
        Fail(activation, "外部参数未全部绑定");
        return;
    }

    if (!EffectSpec.TryCreate(m_Definition, activation.TriggerEntityId, parameters, out EffectSpec spec).Succeeded)
    {
        Fail(activation, "效果输入无效");
        return;
    }

    foreach (EntityId target in targets)
    {
        EffectResult result = m_Session.Runtime.TryApply(target, spec);
        if (!result.Succeeded)
        {
            Report(result);
            continue;
        }

        activation.Applied.Add(new AppliedEffect(target, result.InstanceId));
    }
}

public override void OnTerminate(Activation activation)
{
    foreach (AppliedEffect applied in activation.Applied)
    {
        m_Session.Runtime.TryRemove(applied.Target, applied.InstanceId);
    }
}
```

`OnStart` 无返回值：成败只影响本节点激活状态与诊断，不向图内回传。多目标共享同一份只读参数，但每个目标各自创建效果实例，与 `ApplyMany` 的语义一致。

### 6.2 数值输入器

```csharp
public readonly struct ParameterSlot
{
    public uint EffectDefinitionId { get; }
    public uint ParameterId { get; }
}

public bool TryBuildParameters(GraphNodeData node, out IReadOnlyList<EffectParameter> parameters)
{
    List<EffectParameter> result = new List<EffectParameter>();
    foreach (ParameterBindingData binding in node.Parameters)
    {
        ParameterSlot slot = new ParameterSlot(binding.EffectDefinitionId, binding.ParameterId);
        if (!m_Parameters.TryGetValue(slot, out FP64 value))
        {
            parameters = null;
            return false;
        }

        result.Add(new EffectParameter(binding.ParameterId, value));
    }

    parameters = result;
    return true;
}
```

### 6.3 属性监听器

```csharp
public override void OnStart(Activation activation)
{
    if (!TryResolveTargets(activation, m_Node.Targets, out IReadOnlyList<EntityId> targets))
    {
        Fail(activation, "无法解析监听目标");
        return;
    }

    foreach (EntityId target in targets)
    {
        m_Tokens.Add(m_Session.Subscribe(target.Value, m_Node.Listen.AttributeId,
            change => OnChange(change), m_Node.Listen.Channels));
        m_LastMatched[target] = false;
    }
}

private void OnChange(AttributeChange change)
{
    // 通知处于只读发布阶段：只入队，不写属性、不施加效果。
    bool matched = Matches(change);
    if (matched == m_LastMatched[change.EntityId])
    {
        return;
    }

    m_LastMatched[change.EntityId] = matched;
    m_Runtime.Enqueue(new PendingSignal(m_Node.NodeId, matched ? 0 : 1,
        change.EntityId.Value, change.Frame));
}

private bool Matches(AttributeChange change)
{
    FP64 oldValue = ReadChannel(change.OldValue, m_Node.Listen.Channels);
    FP64 newValue = ReadChannel(change.NewValue, m_Node.Listen.Channels);
    switch (m_Node.Listen.Kind)
    {
        case ListenKind.Changed:
            return oldValue.Raw != newValue.Raw;
        case ListenKind.Increased:
            return newValue > oldValue;
        case ListenKind.Decreased:
            return newValue < oldValue;
        case ListenKind.Reached:
            return oldValue < m_Node.Listen.Threshold && newValue >= m_Node.Listen.Threshold;
        default:
            return false;
    }
}
```

`m_LastMatched` 实现边沿触发：条件持续满足只发一次，离开条件时补发另一个端口。`SubscribeStateReset`、会话替换、实体离场都要重置这个状态，否则恢复后不会重新上报。

### 6.4 条件节点

```csharp
public bool Evaluate(out bool result)
{
    result = false;
    if (!TryResolveTargets(m_CurrentActivation, m_Node.Targets, out IReadOnlyList<EntityId> targets) ||
        targets.Count == 0)
    {
        return false;
    }

    bool value = true;
    foreach (EntityId target in targets)
    {
        if (!EvaluateExpression(m_Node.Condition, target, out bool item))
        {
            return false;
        }

        value = value && item;
    }

    result = value;
    return true;
}

private bool EvaluateExpression(ConditionData condition, EntityId target, out bool value)
{
    value = false;
    if (condition.Kind == ConditionKind.And || condition.Kind == ConditionKind.Or)
    {
        value = condition.Kind == ConditionKind.And;
        foreach (ConditionData child in condition.Children)
        {
            if (!EvaluateExpression(child, target, out bool childValue))
            {
                return false;
            }

            value = condition.Kind == ConditionKind.And
                ? value && childValue
                : value || childValue;
        }

        return true;
    }

    return EvaluateLeaf(condition, target, out value);
}
```

条件求值放在 `Tick` 的只读阶段，不依赖通知时序。属性叶子用 `session.Runtime.TryGetController` 加 `controller.Attributes.TryGetValue` 读取；标记叶子依赖外部标记查询器，未接入时编译期就报错。

### 6.5 信号节点

```csharp
public override void OnStart(Activation activation)
{
    GraphSignalRequest request = new GraphSignalRequest(m_Node.SignalId,
        activation.TriggerEntityId, m_Frame);
    m_Signals?.Invoke(request);
}
```

图内信号是 `GraphSignal` 结构体，系统外信号是 `SignalId` 加外部回调，两者命名分开，避免混淆。

## 7. 参数编号方案（ParameterId 怎么确定）

1. 编号由效果作者在效果资产里分配：复用已有的 `EffectGraphAsset.RequiredParameters`（`uint[]`，非零、效果内唯一，编译期已校验），`EffectDefinition.TryCreate` 也会拒绝 `SetByCaller` 且 `ParameterId == 0` 的修改器。
2. 外部输入节点不手填数字，用复合键 `(EffectDefinitionId, ParameterId)` 定位槽位，编辑器从所选效果的 `RequiredParameters` 生成下拉。同一效果可以挂多套外部参数，不同效果之间也不会撞号。
3. 若以后需要"一个外部变量驱动多个效果的同名参数"，再加一层全局 `ParameterCatalogAsset`（槽位编号加名称），效果与输入器都引用槽位编号，编译期保证两侧一致。首期用复合键即可，不需要新增资产。
4. 运行时用 `SetParameter(slot, value)` 写值；激活前对照 `CompiledEffect.RequiredParameters` 检查是否全部绑齐，缺一个就诊断失败，不把坏输入带进事务。`AttributeSession.TryApply` 自身还会再校验一次必填参数。

## 8. 编译期校验

- `NodeId` 为正且唯一，节点数与边数设上限（可对齐现有 `MaximumNodes = 512`）。
- 每个节点的强制输入槽必须有入边；边两端端口类型必须一致。
- 自环直接拒绝；信号图做拓扑排序，存在静态环即报错（参考现有 `ValidateStaticCycle`）。允许"监听器 → 效果 → 属性 → 监听器"这类跨帧动态环，但运行时用激活数、深度上限截断。
- `Effect` 节点引用的效果必须在本次会话锁定的效果集合内；监听器与条件引用的属性必须在 Catalog 内；条件引用的标记必须在标记库内。
- 标记运行时未接入时，涉及标记的条件或效果在编译期报错，与现有 `MarkerRuntimeUnavailable` 语义一致，不静默降级。

## 9. 与现有运行时的接口

| 用途 | 调用 |
| --- | --- |
| 帧边界 | `session.BeginFrame(frame)` / `session.EndFrame()` |
| 施加效果 | `session.Runtime.TryApply(EntityId, EffectSpec)` 或 `ApplyMany` |
| 中断回滚 | `session.Runtime.TryRemove(EntityId, EffectInstanceId)` |
| 属性监听 | `session.Subscribe(entityId, attributeId, callback, channels)` |
| 恢复重绑 | `session.SubscribeStateReset(...)`、`AttributeRuntimeHost.SessionChanged` |
| 条件读值 | `session.Runtime.TryGetController` 加 `controller.Attributes.TryGetValue` |
| 快照与诊断 | `session.Runtime.CaptureState()`、`RuntimeDiagnostics` |

会话被替换时，图运行时必须释放全部订阅并按新会话重建监听器，否则旧令牌指向已销毁的域。

## 10. 确定性与失败

- 遍历顺序固定：节点按 `NodeId` 升序，入边按作者顺序，目标按 `EntityId` 升序（与 `ApplyMany` 的去重排序一致）。
- 单节点失败只记录诊断，不中断整帧；只有相位错乱或图状态损坏才把图标记为 `Faulted` 并停止推进。
- 每帧每节点激活数、每帧总激活数、图深度都设上限，防止监听器与条件互相触发形成无限循环。

## 11. 需要先拍板的语义

1. `Terminate` 取消"本节点全部激活"，还是只取消携带同一激活编号的上游？当前伪代码按前者。
2. "达到数值"是跨越阈值触发一次，还是等于阈值持续为真？
3. 监听器解锁存后，条件重新满足时是否补发一次？
4. 条件节点 true / false 两个端口是否都发信号，还是只发 true？
5. `Effect` 节点中断时是否移除已施加的持久效果？建议移除，否则中断只停止后续传播，属性仍在。
6. 标记条件是否首期参与，还是等标记运行时接入后再开放。
