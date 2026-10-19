# 运行时属性变化监听

监听实现在 `AshACT.Effects`，由 `EffectRuntime` 收集事务成功提交的变化；`AttributeSession` 提供订阅转发，`AttributeRuntimeHost` 提供会话更换提示。没有新增事件框架、外部输入队列、自动帧推进或配置资产。

## 订阅与读取

```csharp
// 初始化显示时主动读取当前值，订阅本身不合成初始变化。
session.Runtime.TryGetController(new EntityId(entityId), out EffectController controller);
controller.Attributes.TryGetValue(new AttributeId(attributeId), out AttributeValue initialValue);

// 默认只监听 Current；需要同时观察两个通道时显式选择 Both。
IDisposable subscription = session.Subscribe(entityId, attributeId, change =>
{
    // 使用本笔事务携带的旧值、新值和帧，不从浮点显示值反推变化。
    PresentValue(change.OldValue.CurrentValue, change.NewValue.CurrentValue, change.Frame);
}, AttributeChangeChannels.Both);

// 界面关闭或角色视图释放时解除订阅，可重复调用。
subscription.Dispose();
```

示例中的 `PresentValue` 是接入方自己的显示方法。直接使用计算库时调用 `runtime.Subscribe(EntityId, AttributeId, callback, channels)`。订阅、退订、模拟命令和回调均在同一个模拟线程调用；没有跨线程同步能力。

订阅校验实体已注册且实际拥有该属性。空回调、无效通道、未知实体或属性以参数异常拒绝。通道为 `Base`、`Current`、`Both`，默认 `Current`。`AttributeChange` 包含实体、属性、生命周期编号、完整旧新 `AttributeValue`、实际变化通道、整数帧及 `Cause`。判断精确比较 `FP64.Raw`，单个 Raw 单位的差异也报告。

Base 被 Override 遮住时，Base/Both 监听者收到通知，Current 监听者不收到。Both 在同一事务中对同一属性最多回调一次。达到边界后值相同、空效果和最终净值不变的事务均无属性通知。本接口覆盖 Runtime 管理的模拟事务；独立 Store 及首帧前直接配置 Store 的写入不发布通知。

## 提交与发布边界

每笔事务在已有原域和候选域之间比较数值，不捕获完整状态快照、不计算 Hash，不按帧轮询。候选域全部成功才生成净变化并提交所有角色、效果和计数器；失败不发布任何该事务的变化。

通知按事务提交顺序发布，同一批次按实体、属性编号升序，匹配监听者按注册顺序调用。事务内 `100 → 90 → 100` 不通知；同帧两笔独立事务 `100 → 90`、`90 → 100` 保留两条通知。

通知在最外层 Runtime 写入调用结束、帧阶段已确定后同步发布。`ApplyMany` 全部目标处理后发布各个成功目标的独立批次。`TryBeginFrame` 和 `TryEndFrame` 处理到期及周期时，若后续事务失败，先进入 Faulted，再发布此前成功事务的批次；失败事务不发，后面的实例仍不执行。

收到任何回调时，跨角色依赖已一起提交。一次公开调用内有多个事务时，重新读取 Runtime 可能看到后续事务的值，事件自身的旧新值始终表示对应事务。Session 的 LastResult 在 Runtime 返回后赋值，回调使用通知上下文及 Runtime 当前阶段，不依赖 Session.LastResult 表示正在发布的操作。

`Cause.Reason` 区分 DirectCommand、EffectApplied、EffectRemoved、EffectExpired、PeriodicExecution、EntityDeactivated；另带触发实体及已知的来源、定义和实例编号。直接命令或一次清理多个效果的离场事务不伪造单个效果编号。连带变化保留触发整笔事务的上下文，不声称定位了唯一修改器，也不推断伤害、治疗等业务原因。

## 持续、周期与生命周期

瞬时效果施加成功后报告实际 Base/Current 变化。非周期持续效果在施加、实时依赖变化、移除或到期重算后报告变化。周期效果施加时只注册并预检，首次执行为开始帧加周期，在对应帧的 EndFrame 修改 Base；有限效果的到期帧先移除，不再触发周期。周期效果到期不会撤回此前修改的 Base，数值未变就没有属性通知。

订阅跟随同一 Runtime 中稳定的实体 ID，跨离场和复活保留。离场清理效果后的变化照常通知；复活只改变活跃状态和生命周期，不合成属性变化，后续初始化数值的命令继续通知。旧效果的 Frozen 来源仍遵循原有规则，不随复活后来源数值变化。

## 状态恢复与会话替换

`SubscribeStateReset(callback)` 返回独立的释放令牌。`TryRestoreState` 仅在验证并整体提交成功后发送一次 RuntimeStateReset，包含原帧、恢复后帧和阶段，即使数值相同也提示重新读取；失败不发。恢复不发布普通属性差异，原订阅保留。订阅、异常信息、待发布批次均不进入快照或规范 Hash，也不会复制到候选域。

`host.SessionChanged += (previous, current) => ...` 在成功初始化并发布新 Setup/Session 后调用；首次 previous 为 null。接入方在这里 Dispose 旧属性及重置令牌，读取新会话初值并订阅。编译失败不更换会话，也不发 SessionChanged。订阅不会自动迁移；外部仍持有旧会话时，旧会话仍可独立运行，旧订阅必须显式释放。Host 不拥有外部创建的视图订阅，也不在 OnDisable 自动结束会话。

## 回调异常与重入

单个属性或重置回调抛异常会写入 `Runtime.LastNotificationException`，并通过 `NotificationFailed` 提供诊断；继续调用其余监听者，已经成功的事务仍成功。诊断回调自己的异常也被隔离。Host.SessionChanged 的异常通过 Unity Debug.LogException 报告，并继续其他监听者。

发布期间所有 Runtime 写入、帧推进、注册及恢复入口返回 `EffectError.NotificationReentrancy`，受管 Store 直接写入仍拒绝。Host 在属性、重置或会话更换回调期间重新初始化也拒绝，并返回同名配置问题。回调可读取和退订；本批次开始后新增订阅从下一批次生效，已经释放的令牌跳过尚未执行的回调。

需要回调驱动新命令时，由外部战斗系统记录并在下一帧按确定顺序提交。当前模块不实现该输入队列，不依赖监听注册顺序安排模拟写入。

## 验证

`AttributeNotificationTests` 覆盖通道、精确 Raw、净变化、跨角色及资源上限、事务失败、多目标部分成功、周期与到期、较早成功事务和后续 Fault、离场复活与 Frozen 来源、恢复、退订、异常及重入。Unity 配置测试覆盖 Session 转发与 Host 更换会话、重新绑定和异常隔离。

按项目工作流检查本次 C# 范围，并分别筛选 `AshACT.Effects.Tests`、`AshACT.Unity.Tests` 执行 EditMode 回归。验证结果及工具阻塞单独记录，零测试或跳过不视为通过。

2026-10-19 本项目实际验证记录如下，执行目录位于忽略的 `.codex/runs/`：

| 检查 | 结果 | 目录 |
| --- | --- | --- |
| C# 工作流，范围 Effects、Unity | passed：工作流结构与 40 个用例、布局、解决方案构建、空白与大括号检查。 | `20261019T075012Z-d7126ec7` |
| 标准 Editor 工作流，Effects 筛选器 | 目标与编译 passed；CLI 与现有 Pipeline 的测试参数协议不兼容，测试启动 blocked，未执行。 | `20261019T074721Z-0dfc9d0e` |
| 本项目既有 Pipeline HTTP 补充 EditMode，AshACT 筛选器 | 发现 256、执行 256、通过 256；失败、跳过、不确定均为 0。含 Attributes 62、Effects 122、Unity 72，本次新增 25。 | `session-runtime-http-20261019T075140` |

补充测试前核对规范化绝对项目路径、唯一 ready 实例、PID、端口与实时状态，读取实际包内连接及测试协议，使用已有 HTTP 入口与本项目描述文件认证，未输出凭据。新源码对应的 meta 与解决方案同步均由同一项目 Editor 导入生成，没有手改 csproj/sln、安装升级工具、修改第三方包或借其他项目验证。HTTP 通过不覆盖标准工作流 blocked。此次没有场景或配置资源改动，ProjectSettings/ProjectSettings.asset 的用户已有差异保留。
