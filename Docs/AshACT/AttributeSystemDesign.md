# AshACT 属性修改器最终设计方案

状态：属性基础、效果运行时、跨角色依赖、生命周期快照、状态保存恢复、Unity 配置资产和可视化编辑器均已实现。本文按当前源码整理总体方案，配置协议为 Schema 3，效果状态规范字节协议为 2，最近更新日期为 2026-10-19。

阶段实现与验证记录分别见[第一阶段](AttributePhaseOne.md)、[第二阶段](AttributePhaseTwo.md)、[第三阶段](AttributePhaseThree.md)和[第四阶段](AttributePhaseFour.md)。运行时属性通知见[属性变化监听](AttributeNotifications.md)。新增的独立标记系统配置入口见[标记配置与请求草稿](MarkerAuthoring.md)：目前只支持定义、角色产生声明和效果操作草稿，尚未执行请求；旧的效果附带标记、计数与会话清单已移除，没有自动回退路径。物理、碰撞、完整技能系统、业务事件发布、网络输入队列及回滚调度仍由外部战斗系统接入；下文明确区分模块已实现的接口和外部集成约定。

## 1. 依据、范围与前置条件

- 本地 [GAS 中文参考](../../Assets/AshACT/Refs/GASDocumentation_Chinese-main/README.md) 的 4.3–4.5 节提供 Attribute、BaseValue/CurrentValue、GameplayEffect、Modifier 和效果实例等概念。参考目录只有 README 和图片，没有可复用的 Unreal C++ 实现。
- 当前源码分别位于 `Assets/AshACT/Attributes`、`Assets/AshACT/Effects` 和 `Assets/AshACT/Unity`，参考资料保留在 `Assets/AshACT/Refs`。本模块不假设已有 AbilitySystemComponent、GameplayTag 或事件总线。
- 定点数源码来自用户指定的 [Physics2D 分支 FixedPoint 实现](https://github.com/Ash-final-light/AshFramework/tree/Physics2D/Assets/AshFramework/Physics/FixedPoint)，位于 [Assets/AshFramework/Physics/FixedPoint](../../Assets/AshFramework/Physics/FixedPoint/FP64.cs)，保留原有 .meta 文件和计算逻辑；C# 控制流和代码块按项目 Allman 规范展开。Framework.FixedPoint.FP64 使用 long Raw 的 Q32.32，已有独立的 `AshFramework.FixedPoint` 程序集。
- 目标是 Windows 平台内的确定性帧同步，不承诺跨平台逐位一致。单机模式也使用同一套逐帧模拟逻辑。网络传输、预测与回滚调度器不属于属性修改器首期实现。
- 只定义通用机制，不内置 Health、Mana、Attack、MoveSpeed 等具体属性。后续由开发者通过配置创建属性。

文中 Attribute 指角色数值属性；Modifier 指修改属性的运算项；Effect 指应用到目标的一次规则或持续状态。标记由独立标记系统管理；Effect 只声明操作请求，目前请求尚未接入执行。

## 2. 不变量与数据所有权

1. AttributeCatalogAsset 是跨场景共用的属性定义库，只保存全部通用属性定义，不保存角色、效果、实体、标记清单或运行数值。CharacterAttributeAsset 独立选择角色实际拥有的属性、初始值及边界，每个实体创建自己的 AttributeStore。
2. 角色的属性集合在战斗开始时固定。战斗中允许 BaseValue、CurrentValue 和活跃效果变化，不允许动态增删属性。不同角色可以拥有不同属性集合。
3. EffectDefinitionId 标识“同一种效果”；每次施加分配独立 EffectInstanceId。重复施加互不刷新、合并或共享持续时间。效果名字、标签、数值和来源都不作为效果身份。
4. AttributeStore 只保存属性值和修改器索引；活跃效果由效果运行时管理，标记状态不归效果实例所有；当前未实现独立标记状态。
5. 模拟状态仅使用稳定实体 ID、定义 ID、实例 ID、整数帧号和 FP64 Raw。运行计算不得读取 Unity Time、GameObject.GetInstanceID、浮点随机数、资产加载顺序或网络到达顺序。
6. EffectGraphAsset 不保存 SourceProfile 或 TargetProfile。实际来源和目标由施加时传入的 EntityId 决定；读取必须存在于对应角色自己的 Store，通用属性库中存在该定义不代表角色拥有该属性。
7. AttributeSessionSetup 是运行方通过 `new` 创建的普通 C# 对象，不继承 ScriptableObject，不生成 SO，不在 Host Inspector 中配置。它只组装本次初始化需要的配置引用和实体绑定；编译后的会话与可变输入分离。
8. 所有模拟命令按约定顺序执行。命令提交后，其变化对本帧后续命令可见。属性通知保留每笔成功事务的净变化，在最外层 Runtime 调用结束后发布。发布期间拒绝递归写入；通知产生的新命令由外部战斗系统排入下一帧，模块不实现外部输入队列。

## 3. 可视化配置与导出

编辑器已提供节点连接区、配置面板和整数帧时间轴，范围限于属性效果，不扩展完整技能编辑器。开发者可为每个角色选择不同的属性配置；共用定义资产不意味着角色共用运行值。

| 配置单元 | 职责 |
| --- | --- |
| AttributeCatalogAsset | 全项目、跨场景共用的属性定义库；Attributes 引用通用定义资产。 |
| AttributeDefinitionAsset | 稳定 AttributeId、显示信息、Resource/Stat 分类、默认边界；Resource 可引用另一个 Stat 作为动态最大值。 |
| CharacterAttributeAsset | ConfigurationId、角色实际选用的定义引用、初始 BaseValue 和可选的角色专属边界、资源上限；另可引用独立标记库并按名称声明可产生标记，尚未执行标记权限规则。 |
| EffectGraphAsset | 稳定 EffectDefinitionId、持续类型、整数帧时长和周期、标记操作草稿、必填参数、数值与修改器节点；不绑定角色配置。 |
| Modifier 节点 | 连到目标属性，声明 Add、Multiply、Divide 或 Override、优先级和数值输入。 |
| 数值节点 | 常量、SetByCaller、读取来源/目标的 BaseValue 或 CurrentValue，以及确定性的 FP64 算术组合。 |
| 时间轴 | 展示施加、周期触发和到期的整数帧语义。 |

AttributeId 由 AttributeDefinitionAsset 显式分配并在 Catalog 中注册。角色通过引用定义选择自己的属性。效果保存稳定的 AttributeId，编辑器显示“编号 + 名称”，不按显示名或 Unity 实例编号生成属性身份。

节点连线使用 Unity 2022.3 自带 GraphView 的原生 Node、Port 和 EdgeConnector：从输出 Port 按住拖到输入 Port 后松开，选中连线按 Delete 断开，支持 Undo。面板中的 LeftNodeId、RightNodeId 和 ValueNodeId 只读，不提供先点击输出再点击输入的连线方式。

“施加者预览角色”和可选的“目标预览角色”只属于编辑窗口上下文，不写入 Effect。SourceBase/SourceCurrent 只能从施加者预览角色实际拥有、且已注册到当前 Catalog 的定义中选择；未选角色时不能选择来源属性。目标读取和修改器在指定目标预览角色后按该角色筛选，否则显示通用库选项。切换预览角色不会自动替换已有编号，不符合新范围时保留编号并提示。

节点图是配置，不执行任意 C# 回调；运行逻辑由通用 C# 引擎解释或执行导出数据。首版属性读取仅可选择本次效果的来源角色或目标角色，不能任意查找场上实体。持续修改器的来源和目标属性读取均为实时读取；周期效果每次触发也重新读取当时属性。需要固定常量时直接使用常量或 SetByCaller 输入，不在首版提供可切换的属性读取快照模式。来源离场时的冻结快照属于生命周期规则，见第 8 节。

编辑器数值输入最多 4 位小数，显示固定 4 位；编译时直接把十进制文本作为精确分数转换为 FP64 Raw，采用就近舍入、正好居中时取偶数，不经 float/double 中转。运行时保留 Q32.32 精度，不在每步运算后截成 4 位。资产保留十进制文本，编译后的数值使用 FP64，诊断同时显示四位小数和完整 Raw。当前只有进程内编译，没有磁盘二进制导出或网络分发格式。

配置资产使用 SchemaVersion 和 ReleaseVersion；当前接受 Schema 3；Schema 1/2 不能直接作为当前结构接受，参与同次编译的资产发布版本必须与 Catalog 一致。SessionSetup 本身没有独立版本字段。只有零错误才发布只读编译结果；初始化后锁定数值、定义和实体映射，修改资产或 Setup 仅在下一次成功初始化时生效。

编译入口按属性库、单个角色、单个效果和完整会话划分。通用 Catalog 可以独立校验，单个 Effect 不要求预先注册角色；完整会话再检查角色实际集合、实体绑定和效果；标记草稿可独立校验，非空操作仍阻止生成可运行会话。实际来源/目标是否具有被读取或修改的属性，以及跨角色实例关系造成的环，在施加时原子校验。

## 4. 当前运行时结构与初始化

| 类型或模块 | 责任 |
| --- | --- |
| AttributeId、EffectDefinitionId、MarkerId、EntityId | 显式分配的稳定值类型；0 作为无效值，不从名称哈希或 Unity 实例 ID 推导。 |
| AttributeMath | 配置文本精确转换及带溢出检查的安全 FP64 运算。 |
| AttributeStore | 每角色一份；保存固定属性集合、BaseValue、CurrentValue、边界、修改器索引。 |
| AttributeAggregator | 对一个属性的持续修改器做确定性重算与截断；不管理效果时间。 |
| EffectDefinition、EffectSpec | 分别表示只读效果配置和本次施加的来源、SetByCaller 参数等输入。 |
| ActiveEffect | 一次独立施加；保存实例 ID、来源和目标 ID、开始/结束帧、下次周期帧、修改器句柄、来源读取绑定。 |
| EffectController | 持有目标的 Store，提供活跃效果实例查询，不提供标记计数。到期、周期和移除由 EffectRuntime 协调执行。 |
| EffectRuntime | 一个模拟域的串行入口；负责实体注册、帧边界、跨角色依赖与事务、离场/复活、完整域状态保存恢复。 |
| ConfigurationCompiler | 分别编译属性库、角色、效果或本次初始化数据，返回不可变结果或结构化错误。 |
| AttributeSessionSetup | 普通 C# 初始化输入：Catalog、Effects、Entities；由外部战斗系统组装，不是配置资产。 |
| CompiledSessionConfiguration、AttributeSession | 前者保存只读编译数据，后者据此创建各实体独立的 Store 和 EffectRuntime，并按锁定的效果编号施加。 |
| AttributeRuntimeHost | 可选的 Unity 组件；由运行方调用 TryInitialize(setup, out issues)，初始化失败保留原 Setup 和 Session。 |
| EffectRuntimeState、RuntimeDiagnostics | 前者保存只读域快照、规范字节和 Hash，后者从快照生成属性聚合、效果及依赖诊断。 |

实际程序集依赖为 `AshFramework.FixedPoint → AshACT.Attributes → AshACT.Effects → AshACT.Unity → AshACT.Unity.Editor`；需要 FP64 的上层也直接引用 FixedPoint。前三个纯计算程序集均设置 `noEngineReferences: true`。Unity 层负责配置编译和会话适配，Editor 层负责图形编辑与调试。

初始化按以下顺序进行：

1. 战斗系统加载共用 Catalog、本次需要的 Effect，以及各角色的 CharacterAttributeAsset，并分配实际 EntityId。
2. 通过 `new AttributeSessionSetup()` 组装这些引用及 `EntityConfiguration` 数组。实体和效果清单不放回 Catalog，也不另存为 SO。
3. 调用 `ConfigurationCompiler.TryCompileSession(setup, out compiled, out issues)`，成功后 `new AttributeSession(compiled)`；使用 Host 时可由 `host.TryInitialize(setup, out issues)` 完成这两步。
4. Session 按实体编号顺序注册独立 Store。当前实体注册只允许在首帧之前完成，不支持通过修改 Setup 在帧中增加角色或属性。
5. 每帧显式调用 `session.BeginFrame(frame)`，按确定顺序调用 `session.TryApply(definitionId, source, target, parameters, out error)` 等命令，再调用 `session.EndFrame()`。source/target 是实际 EntityId，SetByCaller 是独立的数值参数集合。

没有独立的 DependencyCoordinator 或完整 SimulationCoordinator；跨角色协调与 AttributeChange 发布落实在 EffectRuntime 内。Subscribe 按实体、属性订阅 Base/Current/Both，默认 Current，令牌显式释放；恢复成功发独立重置通知，Host 成功换会话后提示重新绑定。输入排序、物理、碰撞和业务事件发布仍由外部战斗系统负责。

来源读取绑定在活跃实例中有 Live 与 Frozen 两种运行状态。Live 指向来源角色的稳定 EntityId；来源死亡或离场时切到 Frozen 并保存该次生命周期结束时的属性快照。该状态不是策划可切换的捕获模式。

## 5. FP64 数值和属性规则

FP64 采用 Q32.32，模拟时间使用整数帧。统一允许配置的数值边界为 [-1,000,000,000, +1,000,000,000]；每个属性还必须配置更窄或相同的边界。结果超过属性边界时截断；FP64 可表示范围溢出、除零或无效配置使整个相关事务失败，不允许先溢出回绕再截断。

原始 FP64 的加减直接操作 long Raw，乘除也不能直接当作完整的溢出检测入口。属性模块已通过 AttributeMath 使用 BigInteger 宽中间值检查加、减、乘、除、聚合及配置转换，不依赖额外包。乘除结果沿用 FP64 实现向负无穷取整的约定，安全包装层不改写数值语义。检查与舍入规则固定，并通过 Windows 上重复运行及重演验证。模拟不先转 float/double 计算再转回 FP64。

BaseValue 是永久基础值，不代表最大值；CurrentValue 由最新 BaseValue 和活跃持续修改器重算。效果到期移除修改器后重新求值，不用“反向执行”撤销。Resource 的消耗和恢复、周期伤害或治疗修改 BaseValue；Resource 的最大值由独立的 Stat 属性提供，不预置任何具体资源名称。首版 Resource 不直接接收持续修改器，需要持续扩容时修改其最大值 Stat。

资源上限下降时保留绝对数值，超出新上限的 Resource BaseValue 被永久截断，再重新计算 CurrentValue。上限日后恢复不自动补足；上限升高也不按比例增加资源。一般 Stat 的 CurrentValue 截断不反写 BaseValue。

持续修改器按稳定的应用序号遍历并计算：

    A = Σ(Add 数值)
    M = max(0, 1 + Σ(Multiply 数值 - 1))
    D = 1 + Σ(Divide 数值 - 1)
    普通结果 = ((BaseValue + A) × M) ÷ D
    CurrentValue = Clamp(Override 胜者数值，或普通结果)

Multiply 输入为非负的完整倍率；两份 1.5 的持续倍率得到 2，而不是 2.25。Divide 输入至少为 1，因此聚合分母不小于 1；两份 ÷2 作用于 Base 120 时，分母为 3，Current 为 40。Override 优先于其他运算，最高优先级获胜，同优先级取最后应用的实例；移除胜者后重算剩余项。所有中间运算也必须通过溢出检查。

瞬时效果依配置顺序对事务中的 BaseValue 逐项执行 Add、Multiply、Divide、Override。周期效果每次到点也执行一次瞬时 Base 修改；已执行的历史结果不会因来源属性后来变化而追溯修改。因此 Base 100 连续接受两次瞬时 ×1.5 后为 225，而两份持续 ×1.5 得到 Current 200。

## 6. 效果应用、实时依赖与原子事务

一次对单个目标的效果应用顺序如下：

1. 创建该目标专属的应用上下文，分配候选实例 ID；纯瞬时效果成功执行后不留下活跃实例。
2. 验证来源读取属性是否存在于实际施加者，目标读取及修改属性是否存在于实际目标，SetByCaller 必填项是否齐全，数值和除数是否合法。若任何必需属性缺失，整个效果在该目标失败；别的目标独立处理。通用 Catalog 的声明不能替代角色自己的属性集合。
3. 将来源/目标读取节点和目标属性加入候选依赖图。节点声明读取 BaseValue 或 CurrentValue；读取自身 CurrentValue 并修改自身 CurrentValue、跨角色互相依赖等环均拒绝整个应用。
4. 在工作状态中执行瞬时 Base 命令或注册持续修改器及周期计时。持续修改器实时求值；相关来源或目标属性改变时沿依赖图重算。
5. 对命令影响的所有 Store 和跨角色依赖闭包按确定拓扑顺序重算、执行属性和资源边界规则。全部成功才一起提交实例、数值、依赖边和计时器。溢出、除零、依赖环等错误均不留下部分修改。
6. 接口返回 EffectResult，并在最外层 Runtime 调用结束后发布已成功事务的只读变化。变化包含全部跨角色依赖结果，按事务提交顺序、实体及属性编号稳定发布；失败事务不发。通知提出的新写入排入外部系统的下一帧命令队列，通知异常不改变事务结果。

一个多目标技能按 EntityId 依次应用；每个目标作为独立应用单元，某目标失败不撤销其他已成功目标。单目标命令引起的跨角色实时依赖变化则属于同一事务闭包，不能仅提交目标 Store 后再让其他角色重算失败。同步错误返回结构化原因，不依赖异常决定游戏状态。到期、周期调度或死亡清理等必执行系统步骤若无法成功重算，则停止并报告该帧确定性错误，不能悄悄跳过该步骤后继续模拟。

依赖图节点至少包含 EntityId、AttributeId 和 Base/Current 读取类型。相同帧多个属性变化后按确定的节点排序拓扑重算；变化检测比较 FP64 Raw，不以界面 4 位显示精度吞掉变化。活跃效果不采用自动堆叠规则；多个独立实例可以共同向同一属性贡献修改器。

## 7. 60 Hz 帧流程

60 Hz 是外部战斗模拟的集成约定，模块本身只接收从 0 开始连续递增的整数帧，不读取墙上时间或自动推进。已实现的调用协议是：BeginFrame 先移除到期项并进入 Input；调用方提交命令；EndFrame 按实例编号结算周期并回到 BetweenFrames。必执行步骤失败后进入 Faulted 并停止后续写入。

下表保留完整战斗循环的目标顺序。物理 Step、碰撞、输入排序和业务事件发布是外部系统的职责，不是 EffectRuntime 自动执行的阶段；属性通知已在各次最外层 Runtime 调用结束时发布，外部需要帧末表现汇总时自行收集。接入时需明确哪些命令放在 Input 区间内，不能在 EndFrame 后继续调用要求 Input 的写入接口。

| 阶段 | 行为 |
| --- | --- |
| 1. 到期 | 先移除本帧到期的效果、修改器和依赖，重算受影响属性。 |
| 2. 输入/技能 | 命令按 (ControllerId, CommandSequence) 排序；多目标按 EntityId 排序。逐条事务提交，较早命令可影响较晚命令的判定。 |
| 3. 周期与属性 | 活跃实例按稳定实例 ID 处理本帧到期的周期执行，逐次按事务结算；完成本阶段所需的属性重算。 |
| 4. 物理 | 执行一次固定步长的物理 Step。 |
| 5. 碰撞与伤害 | 稳定排序碰撞结果并处理命中、伤害、效果及死亡。此阶段产生的移动属性修改影响下一帧物理 Step。 |
| 6. 发布/记录 | 发布已提交的只读事件，记录确定性状态或校验值；事件引发的新命令进入下一帧。 |

效果在施加当帧生效。持续区间为 [开始帧, 结束帧)，到期帧先移除，不在结束帧额外触发周期。首次周期执行在开始帧 + 周期间隔；周期和时长都用正整数帧数，持续效果可选择有限时长或无限时长。举例：开始帧 10、时长 3、周期 1，只在 11、12 帧触发。空效果可以有独立实例和到期帧，但不产生标记状态。

外部系统接入时，各阶段内部的事件序号、碰撞排序键和重复命令校验须写成协议并纳入测试。属性效果运行时使用稳定顺序，不依赖 Dictionary 或 HashSet 的遍历顺序决定结算结果。

## 8. 死亡、离场和复活

死亡是外部战斗逻辑发出的确定性生命周期命令；属性系统不硬编码 Health 等属性判断死亡。来源角色死亡或以其他方式离场时：

1. 完成导致该次死亡/离场的命令及其属性依赖重算。
2. 在该角色自己的活跃效果清理前，保存此刻所有属性的 BaseValue、CurrentValue 的 FP64 Raw 快照。多个旧效果可引用同一份不可变快照。
3. 将其他目标身上仍存活、以该角色为来源的效果从 Live 绑定切换为 Frozen，解除跨角色实时依赖；它们的剩余时长继续运行。标记系统的生命周期规则不在此处实现。
4. 清除死亡目标自己身上的全部活跃效果和持续修改器，再完成依赖重算。成功提交后发布真实属性净变化；外部系统处理自己的业务事件发布。实体属性订阅跨离场和复活保留，视图所有者负责释放令牌。

如果 A 死亡，A 施加在仍存活的 B 身上的旧效果继续读 A 的死亡快照；A 身上的旧效果全部结束。A 复活后，B 身上这些旧效果仍读旧快照；复活后新施加的效果才实时读 A。A 再次死亡时，新效果冻结于新的死亡快照，旧效果仍保持自己的旧快照。直接离场使用相同规则。

如果目标 B 死亡，B 身上的所有效果结束。B 复活后没有旧效果，复活时具体资源值由战斗逻辑初始化，不由属性修改器自动恢复。若来源和目标同时死亡，生命周期命令按确定的实体/事件顺序处理，但每个来源的快照都在清理自己身上效果之前取得。

## 9. 保存、调试与验收

EffectRuntime 已提供 `CaptureState()` 和 `TryRestoreState(state, definitions)`，保存本模块的实体属性、Base/Current Raw、活跃实例、Live/Frozen 绑定及冻结快照、句柄和应用序号、帧状态、到期/周期计时和域计数器。恢复时校验定义与配置一致性，重建依赖图并检查状态。只读快照提供规范字节和 Hash，用于同帧比较及续演验证。

外部待处理命令、确定性随机状态、物理及其他战斗系统状态不在本模块快照中；完整帧同步重演需要外部系统一并保存。当前不提供磁盘编解码、网络协议、预测或回滚调度器。

调试面板已显示每角色 Base/Current、Raw、边界、A/M/D 汇总、Override 胜者、活跃实例、来源绑定、周期和到期帧、依赖路径及失败原因。窗口内通过普通 AttributeSessionSetup 临时选择 Catalog、效果和实体角色，不保存初始化资产；也可以查看由运行方初始化的 Host。预览运行数值不回写共享定义，程序集重载后临时输入和预览会话需要重建。

图编辑器复用视觉树和 SerializedObject，普通重绘不重建 Node/Port/Edge。调试状态在操作后或手动刷新时采样并缓存，只绘制可见行；可选自动刷新频率为 2 Hz。完整快照采样仍有复制及 Hash 计算成本，不承诺任意规模下的固定性能。

关键回归与验收场景包括：

| 场景 | 预期 |
| --- | --- |
| Base 100，两份持续 ×1.5；两次瞬时 ×1.5 | 前者 Current 200；后者 Base 225。 |
| Base 120，两份持续 ÷2 | Current 40。 |
| 高优先级 Override 与同优先级后施加 Override | 高优先级胜；同优先级后者胜，移除后重算。 |
| A 属性实时驱动 B 的持续修改器 | A 值改变时，B 在同一命令提交中确定性重算；形成环时整个新效果失败。 |
| A 死亡、复活、再次死亡 | B 的旧效果始终读第一次快照；新效果先实时读 A，第二次死亡后读第二次快照。 |
| B 死亡、复活 | B 的效果及其修改器被清除，复活后没有旧效果。 |
| 目标缺少效果要求的属性 | 该目标整个效果失败；其他目标独立成功。 |
| Catalog 声明属性，但实际施加者未拥有该属性 | 来源读取失败，整个效果不产生数值或实例；不存在从通用库补读角色数值的行为。 |
| 同一 Effect 由不同角色施加 | 分别读取实际来源 Store，不要求 Effect 绑定某个角色配置。 |
| 修改角色预览上下文 | 属性选项随角色实际集合变化，原无效编号保留并提示，不自动替换或写入 Effect 的角色字段。 |
| 运行时修改 Setup；尝试用无效输入重新初始化 | 已编译会话保持原实体绑定和数值；Host 失败后仍保留原 Setup 和 Session。 |
| 临时预览初始化并施加效果 | 通过普通对象创建会话，Catalog、CharacterAttribute、Effect 资产内容不变，不生成 SessionSetup SO。 |
| 资源上限降低再升高 | 超出新上限的 Base 永久截断，不自动恢复。 |
| 周期结束边界 | 到期帧无额外触发；已经修改的 Base 不因效果结束回退。 |
| 任一中间 FP64 溢出或除零 | 相关跨角色事务原子失败，数值、实例、依赖和计时器均不改变。 |
| 同一输入与初始快照重复运行 | Windows 上得到相同的 FP64 Raw、实例 ID 和状态哈希。 |

## 10. 当前交付与验证边界

| 阶段 | 已实现范围 | 说明 |
| --- | --- | --- |
| 第一阶段 | FixedPoint 程序集、安全数值入口、属性集合、四种操作、资源规则与原子事务。 | [属性基础](AttributePhaseOne.md) |
| 第二阶段 | 效果定义/实例、独立施加、周期与整数帧调度。 | [效果运行时](AttributePhaseTwo.md) |
| 第三阶段 | 跨角色依赖、实时重算、生命周期冻结、完整模块状态保存恢复与续演。 | [跨角色与状态](AttributePhaseThree.md) |
| 第四阶段 | 通用 Catalog、角色和 Effect 资产、运行时 SessionSetup、原生 Port 图编辑器、原子编译与临时运行调试。 | [Unity 接入与编辑器](AttributePhaseFour.md) |

最近一轮旧标记移除验证为 293 个 EditMode 用例全部通过，实际配置资产完成 Schema 3 迁移并核对配置内容，效果状态规范字节协议为 2；当前执行记录、资产迁移及标准 CLI 测试步骤的兼容性阻塞见标记配置文档。第四阶段文档末尾保留当时 231 个用例和界面检查的历史记录，不与本次结果混用。

本文维护当前设计与代码职责。本次旧标记移除包含源码、配置资产和文档检查，分别执行 C# 工作流、Editor 工作流及本项目 HTTP 补充回归，并检查实际界面。文档工作流检查自身结构与工作流用例，不代替 C# 编译、Editor 测试、视觉验收或完整战斗系统的确定性验证。
