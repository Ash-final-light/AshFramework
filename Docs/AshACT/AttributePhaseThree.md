# 属性模块第三阶段：跨角色依赖、生命周期与完整状态

本阶段承接[第一阶段](AttributePhaseOne.md)与[第二阶段](AttributePhaseTwo.md)，落实[总体设计](AttributeSystemDesign.md)中效果运行时之后的实施步骤。实现位于 `Assets/AshACT/Attributes/Runtime` 与 `Assets/AshACT/Effects/Runtime`；没有新增具体业务属性、Unity 配置资产、场景、Prefab、可视化编辑器或网络调度器。

## 程序集与状态所有权

```text
AshFramework.FixedPoint
    ↑ AshACT.Attributes
    ↑ AshACT.Effects（也直接引用 FixedPoint）
    ↑ 未来的模拟输入、Unity 适配与编辑器
```

运行时程序集仍为 `noEngineReferences: true`，没有 UnityEngine、UnityEditor、默认 Assembly-CSharp 或第三方包依赖。Attributes 通过 `InternalsVisibleTo("AshACT.Effects")` 只向协调层开放工作副本、未重算命令、单节点重算及候选状态提交能力；Attributes 不引用 Effects。

每个角色仍有自己的 `AttributeStore`。`EffectRuntime` 是一个模拟域的串行协调入口，拥有实体注册表、活跃实例、依赖结算、计数器与生命周期状态。依赖图由当前固定属性配置和活跃效果重建，不另存可能失配的可变图副本。生命周期序号从 1 开始，成功复活时递增，溢出明确拒绝。

同一个 Store 不能注册给第二个域或实体。保留首帧开始前直接配置 Store 的能力；从首次成功进入帧协议开始，`AttributeStore.TryExecute` 返回 `StoreOwnedByRuntime`。模拟属性写入使用 `EffectRuntime.TryExecute(entityId, commands)`，该入口也阻止外部移除效果拥有的修改器句柄。独立使用、未纳入模拟域的 Store 保持第一阶段 API 和行为。

没有引入线程、异步回调、通知发布、待处理输入队列或随机状态。调用方仍负责在同一模拟线程上按确定顺序提交命令、碰撞与生命周期事件；这些外部队列和战斗系统状态不属于本模块快照。这里将第三阶段所需的域协调落实在现有运行时中，没有提前扩展完整战斗 SimulationCoordinator。

## 表达式与读取协议

`EffectValue` 保留常量与 SetByCaller，新增有效 AttributeId 的四种读取：

| Kind | 读取对象 | 读取通道 |
| --- | --- | --- |
| SourceBase | `EffectSpec.SourceId` 或该实例的冻结来源 | BaseValue |
| SourceCurrent | 同上 | CurrentValue |
| TargetBase | 当前效果目标 | BaseValue |
| TargetCurrent | 当前效果目标 | CurrentValue |

`EffectValue.Arithmetic(operation, left, right)` 创建不可变二元树，支持 Add、Subtract、Multiply、Divide。定义工厂校验全部节点，限制根深度为 0、最大深度 64、最多 4096 个节点，拒绝无效操作、缺少读取属性编号或缺少表达式。表达式没有用户 C# 回调，不搜索任意实体。

所有加减乘除使用第一阶段 `AttributeMath`，保留负数向负无穷取整的 Raw 语义，并检查每一步中间溢出。表达式中间结果可以超过统一配置输入范围，但必须仍可用 FP64 表示；最终修改器操作数仍受 ±1,000,000,000、非负完整倍率及至少为 1 的完整除数约束。算术表达式中的除数允许负数和小于 1 的非零值，区别于最终 `AttributeOperation.Divide` 的完整除数规则。

持续修改器在来源或目标依赖变化时实时重新求值，替换操作数时保留原句柄、优先级和应用序号。瞬时效果和周期触发按配置顺序执行：每条修改后完成依赖传播，下一条读取可见的最新值。周期施加时预检当前参数、读取属性与操作数，不执行 Base 修改；每次真正触发重新求值，不复用施加时的数值。来源读取仅在来源已注册且存活时允许；为兼容第二阶段，不读取来源属性的常量/参数效果仍允许非零但未注册的来源 ID。

示例中的编号由调用方分配，不在库中预置：

```csharp
EffectValue sourceValue = new EffectValue(EffectValueKind.SourceCurrent,
    attributeId: configuredAttributeId);
EffectValue amount = EffectValue.Arithmetic(EffectArithmetic.Multiply,
    sourceValue, new EffectValue(EffectValueKind.SetByCaller, parameterId: 7));
```

## 依赖图、顺序和跨 Store 事务

依赖节点是 `(EntityId, AttributeId, AttributeReadKind)`，Kind 明确为 Base 或 Current。图包含：

- 每个属性的 Base → Current。
- 资源上限 Stat Current → Resource Base，使永久截断先于资源 Base/Current 的下游读取。
- 持续修改器的每个 Live 来源读取或目标读取 → 被修改属性 Current。
- Frozen 来源没有实时边；目标读取仍保留。

瞬时和周期表达式只在执行时求值，不建立持续依赖。即使 Override 暂时遮住其他修改器，也不删除其依赖边。直接自环、跨角色间接环、经过资源上限的环均在新效果应用时拒绝，拒绝包括全部修改器和标记，不消耗实例、句柄或应用序号。

拓扑排序使用稳定有序集合，同级按 EntityId、AttributeId、Base/Current 升序。每次重算都先完成上游节点，再求该 Current 节点拥有的动态修改器，最后聚合。修改器仍按原应用序号聚合和选择 Override，拓扑顺序不改变持续聚合的第一阶段规则。

每个单目标命令在整个模拟域的候选 Store 与效果索引上执行。命令中的每条属性操作之后执行稳定重算；整批成功后才把候选状态提交到原来的 Store 和 Controller 对象。任一受影响角色出现除零、溢出、缺失属性或非法资源范围，丢弃整批候选状态：数值、实例、标记、绑定、应用顺序、句柄/实例计数器与计时器均不部分提交。当前实现完整复制并重算全域，覆盖全部依赖闭包，优先保证正确性，尚未优化为只复制或重算脏闭包。

`ApplyMany` 保持按 EntityId 去重、排序、逐目标独立提交。单个目标失败不撤销已成功目标。到期与周期仍按全域实例 ID 排序，每个实例事务独立；必执行步骤失败进入 `Faulted`，保留此前成功的实例事务，失败事务回滚，后面的实例不执行，LastCompletedFrame 不前进。没有帧级自动重试或整帧回滚。

## 死亡、离场、复活

生命周期由调用方显式提交，不内置 Health 或任何数值死亡判定。死亡与离场统一调用 `TryDeactivate(entityId)`：

1. 完成现有状态的依赖重算。
2. 在清理目标自己身上效果之前，捕获其当前生命周期的不可变 `SourceSnapshot`，含全部属性 Base/Current Raw。
3. 其他目标上仍为 Live、以该实体为来源的效果切到同一份 Frozen 快照，移除来源实时边。时长、周期、实例 ID、句柄与标记继续。
4. 一次清除该目标身上全部效果及其修改器/标记，再统一重算并提交。外部独立注册的常量修改器仍由其所有者管理。

清理或下游重算失败时，整个生命周期命令回滚，包括来源活跃状态与已经候选切换的绑定。成功离场后实体保留注册和固定属性配置，状态为 inactive；不从域中删除 EntityId，不接受新效果或属性写入。`TryReactivate` 递增生命周期并激活，具体 Base 初始化继续由外部战斗逻辑通过属性命令完成。

旧效果保存自己的快照引用。第一次死亡的效果永远读第一次快照，复活后新效果绑定新生命周期，再次死亡只冻结仍为 Live 的效果。旧的 `ActiveEffect` 查询视图也不会被改写。目标死亡清除自身所有效果和标记，复活不恢复旧效果。若多个实体同帧离场，调用方按约定的实体/事件顺序逐个提交，快照点始终在该实体自身清理之前。

## 完整状态与恢复协议

`CaptureState()` 返回不可变 `EffectRuntimeState`；公开集合只读，配置、Spec、表达式和 ActiveEffect 也保持不可变。状态包含：

| 状态 | 保存内容 |
| --- | --- |
| 协议与帧 | 协议版本 1、CurrentFrame、LastCompletedFrame、Phase、完整 Fault |
| 域计数器 | LastInstanceId、LastModifierHandle |
| 每角色 | EntityId、活跃状态、Lifecycle、固定属性配置完整身份 |
| 每属性 | 初始配置/边界/资源上限、Base/Current Raw、持续命令及其句柄、优先级与应用序号、LastApplicationSequence |
| 每活跃实例 | 实例 ID、定义全部字段/表达式、Spec 来源/参数、目标、开始/到期/下次周期、物化命令、来源生命周期与 Live/Frozen 绑定 |
| Frozen 来源 | 所属实体/生命周期、不可变属性快照；多个旧效果可共享同一份对象 |

属性配置以所有字段的精确相等标识身份，包括初始 Raw、边界、分类与动态上限编号；效果配置以稳定定义 ID 加完整结构身份校验。恢复必须提供战斗锁定的定义清单，拒绝重复定义 ID、缺少活跃定义或相同 ID 的结构变化。当前不建立全局效果资产注册表，调用方在整个战斗中仍必须保证同一定义 ID 含义一致，后续命令的定义版本也由调用方锁定。

恢复步骤：先在目标运行时注册相同实体及属性配置，再调用 `TryRestoreState(state, definitions)`。它在私有候选域中检查配置、Raw 范围、句柄和序号唯一性/上界、实例计数器、帧阶段、效果区间/周期、来源生命周期/冻结配置和修改器归属，重建依赖图并重算；重算后的完整状态哈希必须等于保存状态，不能以重算悄悄修复错误值。成功后一次替换原域状态，失败不改变原域。Input 阶段、BetweenFrames、首次开始前和 Faulted 状态均可保存恢复；恢复 Faulted 仍保持停帧，不允许继续半帧。

`GetCanonicalBytes()` 返回新数组，按固定实体/属性/实例/参数顺序，用 BinaryWriter 写固定整数与 FP64 Raw；SHA256 `Hash` 覆盖完整规范状态。依赖图由上述状态重建并验证，不把字典布局、对象地址、进程 HashCode 或配置加载顺序写入状态。快照共享对象的引用身份不影响规范字节，快照内容和生命周期身份参与比较。

本期协议是进程内只读状态对象的捕获与恢复，规范字节用于完整等价和哈希验证；没有实现磁盘/网络字节反序列化、配置资产版本管理、预测、回滚调度器或战斗输入日志。将来持久化编解码可以基于这些只读结构扩展，不能把当前字节比较 API 当作已实现的网络协议。

## 错误与验证

沿用 `EffectResult` 的目标、实例、修改器、参数、属性和底层 `AttributeResult`；新增 MissingSource、DependencyCycle、EntityInactive、InvalidState、ConfigurationMismatch、ModifierOwnedByEffect。未提交的候选实例不返回有效实例 ID；下游现有实例的错误保留其诊断身份。Store 的跨域注册拒绝复用 StoreAlreadyRegistered；直接写入受管 Store 返回 AttributeError.StoreOwnedByRuntime。

新增 `EffectPhaseThreeTests`，保持第一、二阶段回归测试。测试覆盖四种读取、安全四则/负数 Raw 舍入、动态链/资源边/环、跨域原子失败、应用顺序、多目标独立提交、周期实时值、停帧、死亡清理前快照、复活后的两代快照、旧视图/共享快照、完整状态续演、配置不一致/损坏计数器拒绝和重复执行/不同注册顺序。续演比较完整规范字节和 SHA256，包括每个 Raw、ID、顺序、计时器、绑定及标记定义。

验证命令顺序执行，不并发争用项目锁：

```powershell
python .codex/scripts/workflow.py --project 'D:\UnityProject\AshFramework' verify --task csharp --include Assets/AshACT/Attributes --include Assets/AshACT/Effects
python .codex/scripts/workflow.py --project 'D:\UnityProject\AshFramework' verify --task editor --test-filter AshACT.Effects.Tests
python .codex/scripts/workflow.py --project 'D:\UnityProject\AshFramework' verify --task editor --test-filter AshACT.Attributes.Tests
python .codex/scripts/workflow.py --project 'D:\UnityProject\AshFramework' verify --task docs
```

本地验证日期为 2026-10-19，以下运行目录使用 UTC 时间命名，均位于忽略的 `.codex/runs/`：

| 验证 | 结果 | 运行目录 |
| --- | --- | --- |
| 最终 C# 工作流 | passed：结构、40 个工作流测试、C# 布局、解决方案构建、空白及大括号检查全部通过。 | `20261018T173307Z-a2c096a4` |
| 标准 Editor / Effects | 目标与编译 passed；测试传参 blocked，未启动测试。 | `20261018T173355Z-6410281a` |
| 标准 Editor / Attributes | 目标与编译 passed；测试传参 blocked，未启动测试。 | `20261018T173437Z-99815adf` |
| 本项目 HTTP / Effects | 发现 101、实际执行 101、通过 101；含原有 60 个及本阶段新增 41 个。 | `effect-http-20261018T173057` |
| 本项目 HTTP / Attributes | 发现 62、实际执行 62、通过 62。 | `effect-http-20261018T173508` |

两组实际 EditMode 执行合计 163/163，均零失败、零跳过、零不确定结果。逐项身份与数量和本次发现清单一致。标准 CLI 1.0.0-beta.12 向 embedded Pipeline 0.3.1-exp.1 传测试参数时的 `too old to parse command lines` 限制独立保留为 blocked；HTTP 补充执行不会覆盖这一状态。

补充执行前重新阅读了包内 `Documentation~/connectivity.md`、`Runtime/Models/CommandExecutionRequest.cs` 和 `Editor/Commands/TestCommands.cs`，复用本项目忽略目录中的诊断脚本经已存在的 `/api/exec` 执行。每次调用核对本项目唯一 ready 实例、规范化绝对路径、PID 与端口；认证只从本项目 Library 描述文件读取，不输出凭据；核对测试发现、无其他运行、本次启动回执、完成终态、统计和逐项身份。没有修改标准工作流或第三方包，也没有安装/升级依赖。新增脚本的 `.meta` 和解决方案同步均由同一项目 Editor 生成，没有手改 csproj/sln。

本期只有纯计算逻辑，没有需要视觉验收的场景或资源变更。Windows 当前环境的重复执行与续演验证不构成跨平台逐位一致承诺。本对话在第三阶段交付结束，不自动进入配置资产和可视化编辑器阶段。
