# 配置编辑与运行模块边界

## 当前职责

配置编辑模块可以独立编辑、保存和校验属性、角色、Effect 和标签配置。它不创建属性容器、效果定义或运行会话，不订阅属性变化，不执行标签操作，也不推进逻辑帧。本次拆分没有新增技能节点或信号执行器。

| 程序集 | 目录 | 职责 |
| --- | --- | --- |
| `AshACT.Unity` | `Assets/AshACT/Unity/Runtime` | 配置资产、数值文本、校验问题和作者期校验 |
| `AshACT.Unity.Editor` | `Assets/AshACT/Unity/Editor` | Effect 图、属性与标签配置界面 |
| `AshACT.Unity.Execution` | `Assets/AshACT/Execution/Runtime` | 运行定义生成、初始化输入、Session 和可选 Host |
| `AshACT.Unity.Diagnostics` | `Assets/AshACT/Execution/Diagnostics/Runtime` | 运行快照的属性聚合与依赖诊断 |
| `AshACT.Unity.Diagnostics.Editor` | `Assets/AshACT/Execution/Diagnostics/Editor` | 运行调试窗口、状态展示和运行示例 |

作者期两个程序集不引用 Execution 或 Diagnostics。运行接入程序集依赖配置模块；运行调试程序集可以同时引用配置编辑和运行接入。原有公开类型保留 `AshACT.Unity` / `AshACT.Unity.Editor` 命名空间，但所属程序集和源文件位置已经改变。移动的脚本保留原 `.meta`，配置资产不改写。

配置数据仍使用 `AshACT.Attributes`、`AshACT.Effects` 中的枚举与基础类型以及定点数支持。本次没有继续拆分这些核心程序集；它们所在的 `Data` 目录不表示其中全部都是纯数据。

## 作者期入口

- `ConfigurationValidator.TryValidateCatalog`：属性编号、版本、默认边界和资源上限引用。
- `ConfigurationValidator.TryValidateProfile`：角色属性引用与重复、初值和边界、角色拥有的资源上限、标签声明。
- `ConfigurationValidator.TryValidateEffect`：Effect 编号、整数帧约束、参数声明、节点与连线、数值来源、常量运算和持续目标读取依赖环，以及标签操作配置。
- `MarkerAuthoringValidation`：可以继续单独校验标签库、角色声明和标签操作。

这些方法返回是否通过和 `ConfigurationIssue` 列表，不修改资产，也不要求注册实际实体。错误携带资产、字段路径和节点编号。未完成的图仍可保存为草稿；校验指出缺失连接等问题。正确的标签操作通过作者期校验，不因标签运行时尚未接入而被拒绝。

Effect 编辑器的“校验配置”调用作者期入口，面板列出校验问题。角色配置上下文只用于筛选属性和可产生标签，不创建角色运行状态。时间轴根据整数帧配置展示触发位置，不执行效果。原“运行调试”按钮从 Effect 编辑器移除，调试窗口仍由独立模块的 `AshACT/运行调试` 菜单打开。

## 运行接入入口

`ConfigurationCompiler` 保留原有 `TryCompile`、`TryCompileProfile`、`TryCompileEffect` 和 `TryCompileSession` API。它先调用作者期校验，再构建不可变属性配置、数值表达式和 `EffectDefinition`。Session 组装还检查实体和配置编号冲突。

运行编译仍明确拒绝非空 `MarkerOperations`，返回 `MarkerRuntimeUnavailable`，防止把尚未实现的操作静默丢弃。这是执行能力限制，不是作者期结构错误。Host 初始化失败时保留原会话。

外部可以直接使用 Session 的整数帧入口，不要求实现帧接口；Host 是可选的 Unity 宿主。现有 EffectRuntime 的阶段与事务约束仍然有效。配置编辑模块不依赖这些运行约束。

## 测试归属

- `AshACT.Unity.EditModeTests`：作者期校验、标记编辑、配置非改写、编辑程序集不引用运行接入或诊断。
- `AshACT.Unity.Execution.EditModeTests`：原配置编译、会话、Host、运行诊断、预览回归，以及有效标签草稿在运行编译阶段被明确拒绝的集成测试。

需要同时回归时，使用完整命名空间筛选器 `AshACT.Unity.Tests`，不要只选择旧的单一测试程序集。实际执行结果以本轮 `.codex/runs/` 记录为准，历史文档中的测试通过数不代表本次拆分已通过测试。

## 后续技能编辑器

技能图可以引用 `EffectGraphAsset`，读取公开参数编号，保存目标列表和参数绑定，建立控制信号连接，并通过作者期校验检查引用。此阶段不需要创建 Session、传播信号或执行条件。

技能的公开参数与 Effect 的 SetByCaller 参数分别标识；绑定还需要包含外部 Effect 节点身份，以区分同一 Effect 资产的多次使用。角色对象列表由未来调用方提供，资产保存输入绑定。true/false 分支与 Start/Interrupt 命令应分别建模。上述技能数据结构和执行语义不在本次模块拆分中实现。
