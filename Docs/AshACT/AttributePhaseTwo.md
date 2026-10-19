# 属性模块第二阶段：效果运行时

本阶段代码位于 `Assets/AshACT/Effects`，复用[第一阶段](AttributePhaseOne.md)的 `AttributeStore` 事务和安全 FP64 数值规则。原始总体方案见 [AttributeSystemDesign.md](AttributeSystemDesign.md)。本阶段没有 Unity 配置资产、场景、Prefab 或可视化编辑器。

当前源码已移除旧的效果附带标记模型，标记定义与请求接口见[标记配置](MarkerAuthoring.md)。下面的效果行为按移除后的实现说明；末尾保留当时验收记录。

## 程序集与所有权

```text
AshFramework.FixedPoint
    ↑ AshACT.Attributes
    ↑ AshACT.Effects（同时直接引用 FixedPoint）
    ↑ 后续模拟与 Unity 适配层
```

`AshACT.Effects` 设置 `noEngineReferences: true`，不依赖 UnityEngine、UnityEditor、默认 Assembly-CSharp 或第三方包。`AshACT.Effects.EditModeTests` 只在 Editor 编译，显式引用 Effects、Attributes 和 FixedPoint。第一阶段源码与测试未作修改。

每个战斗模拟域创建一份 `EffectRuntime`，每个角色创建自己的 `AttributeStore`，通过 `TryRegister` 注册稳定 `EntityId` 并取得只读查询入口 `EffectController`。注册只允许在第 0 帧开始前；同域内重复实体和同一个 Store 注册到两个实体均拒绝。来源 `SourceId` 必须非零，但本阶段不读取来源属性，因此不要求来源也在域中注册。

所有写入在同一个模拟线程按协议调用，不提供并发、异步回调或事件发布。调用方负责跨域 Store 所有权，以及外部命令的 `(ControllerId, CommandSequence)` 排序。EffectRuntime 不包含完整 SimulationCoordinator、物理阶段或网络输入队列。

## 只读定义与施加输入

- `EffectDefinition.TryCreate` 校验并复制定义编号、持续类型、时长、周期和修改器配置；修改器保留配置顺序。公开集合只读且不暴露原数组。
- `EffectSpec.TryCreate` 复制来源 ID 与参数，拒绝空对象、零 ID、重复参数与超出统一配置范围的值。定义与 Spec 可以复用；每次成功 `TryApply` 均分配新实例。
- `EffectValueKind.Constant` 使用配置 FP64 常量；`SetByCaller` 使用非零参数 ID，并且所有引用的参数均为必填。缺失参数在对该目标施加时返回 `MissingParameter`。完整倍率非负、完整除数至少为 1，沿用第一阶段规则。
- `SourceBase/SourceCurrent/TargetBase/TargetCurrent/Arithmetic` 等节点在定义工厂明确返回 `UnsupportedValueSource`，不隐式使用零或快照代替。
- `EffectDefinitionId` 只标识配置种类，不建立全局配置注册表。调用方应在战斗开始前保证同编号配置一致并锁定版本，不在战斗期间替换定义含义。

`EntityId` 与 `EffectInstanceId` 使用 ulong，`EffectDefinitionId` 使用 uint；独立标记请求仍使用 uint 的 `MarkerId`，0 均为无效。所有 ID 均不按名称、加载顺序或 Unity 实例编号推导。

## 数值、实例和事务

| 效果 | 施加时 | 后续与移除 |
| --- | --- | --- |
| 瞬时 | 一批 `Instant` 命令按配置顺序修改 BaseValue。 | 不留下 ActiveEffect 或句柄。成功结果中的实例 ID 作为此次执行身份，不能再移除。 |
| 有限或无限、无周期 | 一批 `AddPersistent` 注册常量修改器。 | 仅影响 Stat CurrentValue；移除或到期时一批 `RemovePersistent`，以最新 BaseValue 重算。 |
| 有限或无限、有周期 | 验证必需属性与操作数，登记实例，不执行数值修改。 | 每次到点提交一批 `Instant` 修改 BaseValue；移除不撤销历史触发。Resource 可以接收周期修改。 |
| 空效果 | 可在空 AttributeStore 上独立登记实例。 | 不创建属性或修改器句柄，只保留效果自身的计时和身份。 |

对单个目标，全部参数与属性预检成功后才执行属性事务；属性事务失败不登记实例或计时，也不消耗实例 ID、句柄计数器或 Store 应用序号。周期效果在施加时验证输入和属性存在性；依赖当时 BaseValue 的算术溢出在真正触发时检查，不能通过施加时提前执行一次来改变数值。

EffectController 只查询活跃效果，不从效果数量推导标记。独立标记请求的处理尚未接入运行时。

`ApplyMany` 将目标集合去重，按稳定 EntityId 升序逐个执行，并按该顺序返回每个目标的结果。某个目标失败不回滚其他目标；失败不消耗实例编号，因此成功目标按实际提交顺序得到连续 ID。输入目标列表中的重复 ID 在一次多目标调用中只执行一次；需要重复施加时再次调用。

## 整数帧协议

运行时从 `CurrentFrame = LastCompletedFrame = -1` 开始，只接受从 0 开始逐帧推进，不自动追赶、不接受跳帧、重复帧或负数。调用顺序固定如下：

1. `TryBeginFrame(frame)`：要求处于 `BetweenFrames`，按 EffectInstanceId 升序移除本帧到期的实例。全部成功后进入 `Input`。
2. 调用方在 `Input` 阶段按确定顺序调用 `TryApply`、`ApplyMany`、`TryRemove`，也可提交其他已排序的属性命令。每条命令成功提交后，对后续命令可见。
3. `TryEndFrame()`：按全域 EffectInstanceId 升序执行本帧周期事务。成功后更新 `LastCompletedFrame` 并回到 `BetweenFrames`。

有限效果区间严格为 `[StartFrame, EndFrame)`，`EndFrame = StartFrame + DurationFrames`；溢出时整个施加返回 `FrameOverflow`。无限效果的 EndFrame 为 null。第一次周期在 `StartFrame + PeriodFrames`，开始帧无立即触发，后续间隔不受其他效果施加影响。到期帧先移除，无额外周期。

例如第 0 帧施加时长 4、周期 2 的效果，只在第 2 帧触发一次；时长 5 时在第 2、4 帧触发。周期不小于时长时没有数值触发，效果实例仍存在完整持续区间。`NextPeriodFrame = null` 表示无周期、有限区间内无后续周期，或后续周期超出 long 可表示帧域；使用溢出前判断，不回绕到负帧。达到 long.MaxValue 后不能继续推进。

必执行的到期或周期事务失败时，`Phase` 进入 `Faulted`，`Fault` 保存原始错误，`LastCompletedFrame` 不前进；后续写入统一返回 `Faulted`。较早成功的其他实例事务可能已提交，因此该帧整体不可重试或继续。当前没有帧级回滚；调用方必须停止模拟并诊断。主动 `TryRemove` 或施加失败只拒绝该命令，不锁定运行时。

## 实例与句柄生命周期

`LastInstanceId` 在本域内单调递增，成功的瞬时操作也消耗 ID；失败不消耗，移除不回收。`LastModifierHandle` 同样单调分配，并跳过目标 Store 已使用的句柄。两个计数器达到 ulong 上限时明确返回 `IdExhausted`。句柄只对所属目标 Store 有意义，不作为全局效果身份。

`ActiveEffect` 是不可变视图，记录 Spec、目标、开始/结束/下次周期帧和修改器句柄。周期成功后以新视图替换旧视图；调用者保留的旧视图不会被悄悄改写。`GetActiveEffects()` 返回按实例 ID 升序排列的只读快照。移除后旧视图仅供诊断，需用 `TryGetActive` 查询当前存活性。

`TryRemove(targetId, instanceId)` 必须同时指定所属目标。移除中任一底层命令失败，整个移除事务回滚，原实例保留；移除成功后再移除返回 `MissingInstance`。EffectRuntime 拥有效果创建的句柄，外部不能移除或复用它们；若破坏该约定，底层 Store 的句柄校验会将错误显式返回，系统到期失败会停止模拟。外部直接创建的修改器不会随效果移除。

当前不提供 Dispose 自动清理、目标注销、死亡/离场处理或完整保存恢复；这些必须与后续生命周期、快照和模拟协调阶段一起设计。

## 结构化错误

`EffectResult` 提供 `Error`、`TargetId`、`InstanceId`、`ModifierIndex`、`ParameterId`、`AttributeId` 和底层 `AttributeResult`。配置与应用输入错误使用 Effects 自身的枚举；进入 Store 后的失败使用 `AttributeFailure` 并保留原始错误、属性编号与命令索引。未提交的候选实例不返回有效 ID。所有预期的配置、属性、参数和调度错误均通过结果返回，不依赖异常决定模拟状态。

## 最小调用示例

下列代码假设 `store` 已用第一阶段工厂成功创建，`spec` 已用上述定义与 Spec 工厂成功创建，调用方检查每次结果：

```csharp
EffectRuntime runtime = new EffectRuntime();
EntityId target = new EntityId(100);
EffectResult registration = runtime.TryRegister(target, store, out EffectController controller);
if (!registration.Succeeded)
{
    return;
}

EffectResult begin = runtime.TryBeginFrame(0);
if (!begin.Succeeded)
{
    return;
}

EffectResult applied = runtime.TryApply(target, spec);
if (!applied.Succeeded)
{
    // 拒绝这一条命令；调用方可继续处理本帧其他输入。
}

EffectResult end = runtime.TryEndFrame();
if (!end.Succeeded)
{
    // 停止模拟，记录 runtime.Fault，不重试本帧。
    return;
}
```

## 验证

测试位于 `Effects/Tests/Editor`，命名空间 `AshACT.Effects.Tests`。覆盖定义/参数防御性复制、未支持节点、独立实例、空效果、持续/瞬时/周期与 Resource 行为、Override 顺序、移除重算、到期边界、整批原子失败、多目标排序、句柄共存、帧协议、调度错误停止与重复模拟的 Raw/ID/句柄/计时一致性。

```powershell
python .codex/scripts/workflow.py --project D:/UnityProject/AshFramework verify --task csharp --include Assets/AshACT/Effects
python .codex/scripts/workflow.py --project D:/UnityProject/AshFramework verify --task editor --test-filter AshACT.Effects.Tests
python .codex/scripts/workflow.py --project D:/UnityProject/AshFramework verify --task docs
```

标准 Editor 工作流的目标核对与编译通过，但 CLI 1.0.0-beta.12 向当前 Pipeline 0.3.1-exp.1 传递测试参数时，报包版本不能解析命令行，测试步骤仍为 blocked。没有去掉筛选器重试，也没有安装或升级工具、Pipeline、项目依赖。

已按包内 `Documentation~/connectivity.md`、`CommandExecutionRequest` 和 `TestCommands` 的真实协议，通过同一项目、同一 ready Editor 的现有 `/api/exec` 接口执行筛选测试，作为单独的补充证据。每次调用核对唯一实例、规范化项目路径、PID 与端口；认证只从本项目 Library 描述文件读取，不输出凭据。测试先发现、确认无其他运行、核对启动回执，再检查完成状态、统计与逐用例身份。第一阶段 62/62、阶段二最终 60/60 实际通过，均零失败、跳过与不确定结果。补充 HTTP 成功不改写标准 CLI 工作流的 blocked 状态。

本次验证发生在本地 2026-10-19，执行记录使用 UTC 命名，存于忽略的 `.codex/runs/`：

| 验证 | 结果 | 执行记录目录 |
| --- | --- | --- |
| C# 工作流（最终源码） | passed：结构、工作流测试、布局、解决方案构建、空白与大括号全部通过。 | `20261018T163625Z-4d8629bb` |
| 标准 Editor 工作流 | 目标与编译 passed；测试传参 blocked，未执行。 | `20261018T163710Z-33f4b961` |
| 第一阶段 EditMode 补充执行 | 发现 62，执行并通过 62。 | `effect-http-20261018T163125` |
| 第二阶段 EditMode 补充执行 | 发现 60，执行并通过 60。 | `effect-http-20261018T163750` |

标准工作流目录含 `report.md/report.json` 与逐命令日志；补充执行目录含 `report.json`、本次发现、启动回执与逐项测试结果。HTTP 验证脚本保留在忽略的 `.codex/runs/phase-two-pipeline-tests.py`，未扩展或替换项目工作流，也未修改第三方包。

本阶段不实现跨角色实时读取、依赖图、死亡冻结快照、完整状态恢复、可视化编辑器、网络预测或回滚调度器。重复模拟测试仅验证当前 Windows Editor 中本模块的确定性，不等同于完整网络帧同步或跨平台逐位一致验收。
