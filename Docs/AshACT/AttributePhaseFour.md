# 属性模块第四阶段：Unity 配置、效果图与运行调试

本阶段承接[第三阶段](AttributePhaseThree.md)，实现[总体设计](AttributeSystemDesign.md)最后一步的首期闭环。代码位于 `Assets/AshACT/Unity`，示例资产由本项目 Editor 的 AssetDatabase 创建。没有内置任何业务属性、扩展技能系统、安装包、升级 Unity 或改动场景与 Prefab。

## 程序集与职责

```text
AshFramework.FixedPoint → AshACT.Attributes → AshACT.Effects
                  三个计算程序集保持 noEngineReferences: true
                                    ↓
                              AshACT.Unity
                      序列化资产 / 编译 / 会话 / Host / 诊断
                                    ↓
                          AshACT.Unity.Editor
                      Inspector / 节点图 / 时间轴 / 调试
                                    ↓
                       AshACT.Unity.EditModeTests
```

Unity 版本为 2022.3.62f1c1；配置面板使用 IMGUI、SerializedObject、Undo，画布使用此版本自带的 UnityEditor.Experimental.GraphView 原生 Node、Port、EdgeConnector。已核对本机 GraphViewModule 的真实 API，不依赖 Unity 6 API 或额外图编辑包。前三阶段源码没有修改。

## 配置资产与稳定编号

通过 `Assets / Create / AshACT` 创建以下资产，或用 `AshACT / 创建示例配置` 一次创建可运行的通用示例。

| 资产 | 内容 |
| --- | --- |
| AttributeDefinitionAsset | AttributeId、显示名、Stat/Resource、默认上下界、可选资源上限属性编号。 |
| CharacterAttributeAsset | ConfigurationId、选择的属性定义、每个初始 Base，以及可选的角色专属上下界和资源上限。 |
| EffectGraphAsset | EffectDefinitionId、时长、周期、标记、必填参数、数值节点与修改器。不绑定来源或目标角色。 |
| AttributeCatalogAsset | 跨场景共用的全部通用属性定义。只保存 Attributes，不保存角色、效果、实体或标记清单。 |

上述配置资产都有 `SchemaVersion` 和 `ReleaseVersion`。当前协议为 Schema 2；发布版本必须为正数且参与同次编译的资产与共用属性库一致。版本 1 不自动当成新结构接受。当前项目示例已通过 Editor 的 AssetDatabase 迁移到版本 2：Catalog 仅保留属性定义，Effect 删除 SourceProfile，保留稳定编号、角色初值、图连接和画布位置。旧 SessionSetup 资产在检查无资源引用并备份后，通过同一项目的 AssetDatabase 移除。没有从名称计算编号、自动生成战斗实体 ID 或扫描加载资产充当运行目录。编号由项目开发者分配并持久保存，0 无效。EntityId 为 ulong，其余领域编号为 uint；节点编号是效果图内的正整数连接身份。

`AttributeSessionSetup` 是普通 C# 类，由战斗或角色系统在运行时通过 `new` 创建，不继承 ScriptableObject，也不保存为 SO、场景字段或配置文件。它没有独立的 SchemaVersion/ReleaseVersion，编译版本由 Catalog 及实际引用的配置资产共同决定。

| 运行时输入字段 | 由调用方提供的内容 |
| --- | --- |
| Catalog | 共用的 AttributeCatalogAsset；定义系统可用的属性，不保存本次参战角色。 |
| Effects | 本次会话允许使用的 EffectGraphAsset 数组；按 EffectDefinitionId 编译和查找。 |
| Entities | EntityConfiguration 数组，每项是实际 EntityId 与 CharacterAttributeAsset 的绑定；同一角色配置可用于多个实体，各自创建独立 Store。 |
| Markers | 本次会话声明的 MarkerConfiguration 数组，包含 MarkerId 和显示名；效果引用的标记须在其中声明。 |

调用方可以复用配置资产，但每次初始化应明确本次实体和效果清单。Setup 是可变输入，CompiledSessionConfiguration 才是已锁定的只读结果；后续修改 Setup 不会直接改变已创建的会话。实体注册仍限于首帧之前，修改数组不会在运行中注册新角色。

示例菜单只生成两个属性定义、一个角色、一个 Effect 和一个 Catalog 共五个资产，并临时组装预览输入。总体职责和外部战斗系统的边界与[总方案](AttributeSystemDesign.md)一致。

通用库定义“系统有哪些属性”，角色资产定义“这个角色真正拥有哪些属性”。通用库中存在某属性不代表每个角色拥有它。两个实体可以使用同一角色配置，初始化时仍各建独立 AttributeStore；不同角色可以选择不同属性集合和边界。

Effect 没有 SourceProfile 或 TargetProfile 字段。施加时由调用方传入实际来源与目标 EntityId，读取或修改必须存在于对应实体的实际 AttributeStore。角色缺属性时整次施加原子失败。SetByCaller 节点负责数值参数；来源和目标实体通过施加接口的独立参数传入。运行期实例号和句柄仍由 EffectRuntime 分配。

数值字段保存十进制文本，最多四位小数、范围 ±1,000,000,000。编译先通过 `DecimalValue.TryParse` 限定精度，再直接调用 `AttributeMath.FromDecimalText` 转成 FP64 Raw，正好居中取偶，不经过 float、double 或区域设置转换。诊断显示用 BigInteger 从 Raw 取四位小数，另列完整 Raw；运行计算保留 Q32.32 精度。节点画布的 Vector2 和连线坐标只控制界面排版，不进入运行配置。

## 效果节点编辑

选中 EffectGraphAsset，在 Inspector 点击“打开效果节点编辑器”，或使用 `AshACT / 效果节点编辑器`。左上选效果图和共用属性库；右侧选择“施加者预览角色”，可选“目标预览角色”。这些角色引用只属于编辑窗口上下文，不保存到 Effect。

1. 从“新增数值节点”选择 Constant、SetByCaller、SourceBase、SourceCurrent、TargetBase、TargetCurrent 或 Arithmetic，点击“添加”。Arithmetic 的操作支持 Add、Subtract、Multiply、Divide。
2. 按住节点的输出 port，拖到 Arithmetic 的 L/R 输入 port 或修改器的数值输入 port 后松开建立连线。输出允许多个连接，每个输入只接受一条连接，拖入新线替换旧输入。拖到空白处不创建连接；选中 Edge 按 Delete 断开，Undo 可恢复。画布支持滚轮缩放、中键平移、拖动节点和“适配画布”。没有先点击输出再点击输入的连线模式；面板中的连接编号只读。重复引用节点导出为不可变表达式树；表达式展开仍受运行时深度和数量限制。
3. 在右侧通过“选中节点”下拉框或点击画布标题选择节点。面板只显示该类型需要的字段，支持删除选中节点；节点编号可显式修改，输入引用通过 port 修改并在面板中只读。Constant 使用十进制文本；SetByCaller 的 ParameterId 必须同时列入 RequiredParameters。SourceBase/SourceCurrent 的属性下拉框只提供施加者预览角色实际选用且已在 Catalog 注册的定义；未选预览角色时不能选择来源属性。TargetBase/TargetCurrent 和修改器在选了目标预览角色后按该角色筛选，否则提供通用库属性。选项按稳定编号排序，切换预览角色不会自动替换原编号，原编号无效时显示警告。实际角色仍在运行时校验。
4. 点击“添加修改器”，在“选中修改器”面板选择目标属性，编辑 Add/Multiply/Divide/Override 与 Priority，通过 port 指定数值输入；ValueNodeId 在面板中只读。添加顺序就是瞬时/周期操作顺序。Priority 用于持续 Override；同优先级仍按实际应用顺序判胜。
5. 配置 DurationKind、DurationFrames、PeriodFrames、MarkerIds。有限时长为正数；瞬时/无限时长字段为 0，周期为非负整数帧，瞬时周期必须为 0。底部时间轴展示从相对帧 0 施加、前八个有效周期与到期；有限区间为 `[0, DurationFrames)`，到期帧不触发周期。
6. 点击“校验 / 编译”。编译成功展示锁定协议/发布版本；失败显示错误代码、资产、字段路径与节点编号。单个 Effect 的编译无需实体注册；已选来源预览角色时，另检查该角色是否具备所有来源读取属性。运行调试窗口编译当前临时 SessionSetup，并提供完整错误列表。保存按钮通过 AssetDatabase 保存资产；编辑记录支持 Undo。

编辑面板是序列化配置编辑器，不执行任意 C# 节点。允许暂时输入无效文本或悬空连接以便编辑，校验失败时不能生成新会话。删除节点不会悄悄给其引用填入其他节点；悬空连接会被明确拒绝。

## 原子编译与校验边界

编译入口按职责拆分，均在候选数据中校验，只有零错误才发布只读结果；失败时输出为 null。ConfigurationIssue 包含 Code、Asset、Path、Message、NodeId。

| 接口 | 检查范围与结果 |
| --- | --- |
| `TryCompile(catalog, out compiled, out issues)` | 仅通用属性定义，得到 CompiledAttributeCatalog。无关角色或效果的错误不会阻止属性库编译。 |
| `TryCompileProfile(catalog, profile, out compiled, out issues)` | 在通用库中校验角色实际选择的属性、初值和角色边界。 |
| `TryCompileEffect(catalog, effect, out compiled, out issues)` | 在通用库中校验通用效果图；不要求绑定任何角色或实体。 |
| `TryCompileSession(setup, out compiled, out issues)` | 校验本次运行清单、实际注册角色、效果和标记，得到 CompiledSessionConfiguration。 |
| `TryCompileSession(catalog, entities, effects, markers, out compiled, out issues)` | 运行方直接提供初始化数组，也可以省略 SessionSetup 对象包装。 |

同一角色资产可被多个实体使用；不同资产不能使用相同 ConfigurationId。只编译本次实体实际引用的角色，Catalog 不再注册所有角色或所有效果。

检查覆盖：目录内同类编号非零/唯一、重复属性、必需资产引用、协议/发布版本一致、四位小数、初始值与上下界、分类、资源上限必须为本角色的 Stat、动态初始范围、必填参数声明、标记声明、目标属性和四种读取、修改器合法性、节点编号/输入缺失/表达式环/孤立节点、常量四则中的溢出或除零、完整倍率与除数，以及运行定义工厂的全部规则。

持续效果静态建图包含目录属性 Base → Current、目标读取 → 被修改属性 Current；拓扑检查不求值参数，因此不会因临时数值错误漏掉环。单个效果中不依赖具体目标配置的必然环在编译拒绝。不同目标可覆盖资源上限引用，因此上限 Stat Current → Resource Base 由实际目标在施加时建立并检查，不能用某个配置的默认上限替所有目标提前判环。来源与目标是否为同一实例、不同效果组合、跨角色实例形成的环继续在运行时施加时校验，整个相关事务原子拒绝。动态参数/属性表达式的具体数值合法性继续由运行时安全 FP64 运算和事务检查，不承诺编译能证明所有动态输入永远有效。

首期规模限制：每个目录集合、角色属性、标记或必填参数最多 1024 项；单效果最多 512 个图节点、128 条修改器；每条表达式展开最多 4096 节点、根深度 0、最大深度 64。超过限制返回结构化错误，不截断配置。共享图节点会被复制到运行表达式；限制按展开后的表达式计算。

成功结果只包含只读角色配置、效果定义、实体映射和版本，不保留可变 ScriptableObject、节点数组或角色数值引用。此阶段提供进程内编译，不新增磁盘二进制导出格式或网络配置分发协议。

## Unity 接入与版本锁定

运行方显式组装普通 `AttributeSessionSetup`，调用编译器并创建 `AttributeSession`；使用 `AttributeRuntimeHost` 时调用 `TryInitialize(setup, out issues)`。Host 的 Setup 是运行时只读属性，Inspector 不配置初始化清单。不需要为了使用模块创建场景或 Prefab。编译失败保留 Host 的原 Setup 和会话，编译成功才一起发布新输入和完整新域。无参 `TryInitialize(out issues)` 使用上次成功提交的 Setup 重新初始化，首次初始化仍须由调用方提供数据。

```csharp
// 这些配置引用和实体绑定由战斗系统提供，实体编号来自本次实际运行。
AttributeSessionSetup setup = new AttributeSessionSetup
{
    Catalog = catalog,
    Effects = effects,
    Markers = markers,
    Entities = entities
};
if (!host.TryInitialize(setup, out IReadOnlyList<ConfigurationIssue> issues))
{
    // 向调用方展示结构化配置错误。
    return;
}

AttributeSession session = host.Session;
EffectResult begin = session.BeginFrame(0);
if (!begin.Succeeded)
{
    return;
}

EffectParameter[] parameters =
{
    new EffectParameter(7, FP64.One)
};
if (!session.TryApply(1001, 1, 2, parameters, out string error))
{
    // 记录本次施加失败，运行状态不会部分提交。
}

EffectResult end = session.EndFrame();
if (!end.Succeeded)
{
    // 必执行结算失败时停止模拟，并读取 Runtime.Fault。
    return;
}
```

示例属性和效果 ID 来自示例资产；EntityId 由运行方分配，示例预览临时使用 1/2。计算库不内置这些业务编号。游戏应使用自己的稳定 ID 与确定性命令顺序。运行时修改 Setup 数组、角色绑定或资产值不会改变已编译会话；下一次初始化才读取新的输入。实体仍遵循既有运行时注册与整数帧协议，本次不扩展帧中注册规则。

会话 Configuration 在构造后固定。`TryApply` 只能通过本会话的稳定定义编号查找已编译 EffectDefinition，校验 RequiredParameters，再调用既有 EffectSpec/EffectRuntime。调用中的 source 和 target 是运行时确定的 EntityId，不通过 FP64 数值参数传递角色配置。来源属性缺失、目标属性缺失、读取缺失或依赖环都会整笔失败，不留下部分数值、标记或实例。`Execute`、`Deactivate`、`Reactivate` 仍调用第三阶段事务入口。会话公开 Runtime 用于既有集成及保存恢复；直接调用底层 Runtime 的调用方仍负责遵守锁定定义清单，恢复时应传入本会话 Configuration.Effects 中的 Definition。

没有 Update/FixedUpdate 自动推进、Unity Time 输入、全局角色数值单例或网络调度。调用方在同一模拟线程显式调用 BeginFrame/EndFrame，并检查每次返回值。编辑资产仅影响下一次成功初始化，即使继续使用相同的 ReleaseVersion 数字，已经创建的会话仍保持旧内容；建议发布新内容时递增发布版本以方便外部追踪。

## 真实运行调试

`AshACT / 运行调试` 打开“临时预览配置”折叠面板，在窗口内选择 Catalog，添加效果、实体编号与角色配置、标记，点击初始化。没有 SessionSetup 资产选择框或保存入口；输入只保存在窗口的普通运行时对象中，程序集重载后需重新填写。初始化成功会收起临时配置，避免影响运行状态列表的可见范围。

也可从效果图点击“运行调试”：将当前 Catalog、Effect 和预览角色带入临时输入。预览实体临时编号为 1/2，目标预览角色未指定时沿用来源预览角色；未选来源时需要在临时面板补充角色。预览标记根据该效果的 MarkerIds 去重填入，仅用于调试；正式运行仍由战斗系统提供清单。窗口还可以选择场景中的 AttributeRuntimeHost，读取已由运行方初始化的真实 Session。初始化和重新初始化均为显式操作，只读显示不会修改配置资产。

开始下一帧 → 施加/离场/复活 → 结束本帧，与既有整数帧协议一致。参数输入仍是四位小数文本。重复点击施加产生独立实例，不刷新旧效果。可以手动触发失败以查看错误，面板展示当前帧、完成帧、Phase、完整状态 Hash、Fault 和最近操作消息。

面板在初始化或调试操作后，从 `Runtime.CaptureState()` 采样一次并缓存全部显示文本。默认手动刷新；查看外部 Host 时可点击“刷新状态”，或开启每秒两次的自动刷新。普通重绘、滚动不重新复制状态、计算哈希、聚合或格式化 Raw。面板从同一次只读快照展示：

- 实体活跃状态与生命周期；每属性 Base、Current、Raw、静态边界、动态上限编号。
- 真实已物化持续命令的 A/M/D、完整 Raw、Override 胜者句柄/优先级/应用顺序，以及逐修改器句柄、数值和顺序。
- 活跃实例、定义、来源 ID、Live/Frozen 绑定与来源生命周期、开始/到期/下次周期/周期帧、标记。
- Base/Current 和资源上限边，以及活跃非周期效果的实时读取边。按稳定节点顺序列出，可沿公共节点追踪依赖路径；Frozen 来源不再贡献来源实时边，周期效果也不伪装成持续依赖。

RuntimeDiagnostics 不读取配置资产猜测数值，不写回 Store；聚合诊断使用同一安全运算规则。调试窗口有意显示完整 Raw 以区分四位小数下看似相同的不同运行状态。列表使用固定行高，只绘制可见行，长行支持水平滚动。节点窗口复用 SerializedObject、时间轴文本与 GraphView 保留模式视觉树，仅在编辑、切换资产、Undo 或聚焦时重建；普通重绘不重建 Node/Port/Edge。单次显式采样仍需复制完整域并计算哈希，大规模运行域可保持手动刷新。

## 测试与实际验收

EditMode 测试位于 `Assets/AshACT/Unity/Tests/Editor/ConfigurationTests.cs`，覆盖精确转换与显示取偶、配置错误原子拒绝和定位、角色集合/边界/Store 隔离、版本锁定、静态表达式/依赖环、常量安全四则、跨实体运行环、必填参数、周期到期边界、Frozen 依赖、真实聚合/Override，以及 AssetDatabase 创建/保存/重载、图连接修改、调试会话初始化和失败后保留原会话。

本项目顺序执行以下命令，结果记录在忽略的 `.codex/runs/`；标准 CLI 与 HTTP 补充测试的状态分别记录，不将发现清单、零测试或旧结果视为通过。

```powershell
python .codex/scripts/workflow.py --project 'D:\UnityProject\AshFramework' verify --task csharp --include Assets/AshACT/Unity
python .codex/scripts/workflow.py --project 'D:\UnityProject\AshFramework' verify --task editor --test-filter AshACT.Unity.Tests
python .codex/scripts/workflow.py --project 'D:\UnityProject\AshFramework' verify --task docs
```

本地实现验收日期为 2026-10-19。以下保留运行时 SessionSetup 调整时的代码与实际界面验证记录；后续仅修改说明文档时不把这些记录表述为重新运行测试。

| 验证 | 结果 | 运行目录 |
| --- | --- | --- |
| C# 工作流 | passed：结构、40 个工作流用例、布局、解决方案构建、空白与大括号检查。 | `20261019T041303Z-278554c8` |
| 标准 Editor 工作流 | 项目目标与编译 passed；测试传参兼容性 blocked，未启动测试。 | `20261019T041401Z-c1d08629` |
| 本项目 HTTP EditMode | 发现 231、执行 231、通过 231；失败、跳过、不确定均为 0。其中 Unity 配置/Editor 68、Effects 101、Attributes 62。 | `session-runtime-http-20261019T041445` |
| 实际调试界面 | 点击初始化、开始第 0 帧、施加效果成功；目标 Base 100、Current 200，有 1 个实例；五个资产序列化内容与操作前逐项一致。 | `session-preview-ui-result.json` |

最初 CLI 1.0.0-beta.12 的 `status` 曾漏报实例，`pipeline list` 与 `/api/editor_status` 交叉确认本项目同一 PID、端口与 ready 状态；后续标准工作流的目标与编译均通过，但 CLI 与 embedded Pipeline 的测试参数兼容性仍使测试步骤 blocked。本次补充调用逐次交叉核对规范化绝对项目路径、唯一运行实例、PID、端口及实时 Editor 状态后，使用本项目原有 Pipeline HTTP 入口。认证只读取本项目 Library 的描述文件，不输出凭据。不安装或升级工具、不修改第三方包、不借其他项目验证。标准工作流和补充测试分别记录，HTTP 结果不覆盖标准工作流 blocked 状态。

本次重点覆盖普通对象编译、ulong 实体编号精度、编译后实体绑定与角色数值锁定、Host 初始化失败后保留原输入和会话、临时预览不修改资产，以及原有的属性归属、Port 连线、重绘缓存、整数帧和原子失败回归。示例资产通过本项目 Editor 创建和删除，确认只生成五个配置资产。

用户已有的 `ProjectSettings/ProjectSettings.asset` 差异保留，不纳入功能改动。机器检查、实际界面检查和标准工作流阻塞分别记录。

实际界面已检查临时 Catalog、效果、实体编号与角色、标记各输入区域，无 SessionSetup 资产选择框。成功初始化后面板自动收起，运行列表可见；通过实际按钮执行开始帧和施加，展示真实 Raw、实例与依赖边。Console 保留的 InvalidOperationException 栈位于既有 Pipeline 的 TestResultCollector.RunFinished 重复完成回调，未修改第三方代码，也不将补充测试通过表述为 Console 无错误。
