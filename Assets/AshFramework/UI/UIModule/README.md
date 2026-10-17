# UIModule

uGUI 窗口的按需加载、参数绑定、分层显示、隐藏复用与关闭释放。Unity 2022.3，标准 Task，无额外异步或资源包依赖。

## 已确认的代码存放规则

框架：`Assets/AshFramework/UI/UIModule/`，命名空间 `AshFramework.UI.UIModule`。

```text
UIModule/
|-- Runtime/
|   |-- Contracts/       公共身份、状态、服务接口
|   |-- Core/            注册、请求调度、窗口记录、层内顺序
|   |-- Loading/         加载接口、租约、Resources 适配器
|   |-- Views/           UIView、UIWindow、实例上下文
|   `-- Components/      RootUI
|-- Editor/              根节点创建、Inspector、代码生成器
`-- Tests/
    |-- Editor/         生命周期、并发、释放、生成文件保护测试
    `-- Support/        可被场景对象挂载的测试专用 UIView 程序集
```

业务代码默认生成到以下位置，生成器允许选择其他 Assets 子目录与命名空间：

```text
Assets/GameScripts/UI/Inventory/
|-- Generated/
|   `-- InventoryWindowBase.g.cs    可重新生成
|-- InventoryWindow.cs             只在首次创建时生成，之后手写业务
|-- InventoryArgs.cs               只在首次创建时生成，之后手写参数
`-- InventoryView.cs               只在首次创建时生成，之后维护控件与渲染

Assets/Resources/UI/Inventory.prefab  默认计划资源位置
```

默认业务命名空间为 `Game.UI.Inventory`。业务脚本通过自动引用的框架运行时程序集访问 API；如果业务自己使用 asmdef，需显式引用 `AshFramework.UI.UIModule`。

生成器只写 C#，不自动创建/覆盖 Prefab，不扫描子控件。Prefab 路径用于生成资源地址，必须位于某个 Resources 子目录；加载器本身可替换。

## 编辑器操作

1. 在 Hierarchy 右键选择 `AshFramework/UIModule/RootUI`，创建三层 Canvas 和未激活的 StagingRoot。也可从 GameObject 主菜单进入。
2. 根节点包含 RootUI 组件，Inspector 组件标题右键选择 `AshFramework/UIModule/生成 Window 基类`，或点击 Inspector 中的同名按钮。
3. 输入不带 Window 后缀的窗口名，例如 Inventory；选择命名空间、脚本目录、计划 Prefab 路径和显示层级。
4. 点击“生成 / 更新基类”。编译后将对应 InventoryView 挂到窗口 Prefab 的根节点，并在 Inspector 配置自己的控件引用。
5. 在游戏启动逻辑中注册窗口并创建服务，再通过 IUIService 使用。

生成选项通过 Unity 编辑器序列化保存；生成脚本触发重编译后，窗口名、命名空间、输出路径和显示层级应继续保留。

RootUI 也可在 Add Component 的 `AshFramework/UIModule/RootUI` 中找到；单独添加组件时需手动配置三个 Canvas 与 StagingRoot，创建完整层级请用 Hierarchy 菜单。

根节点创建支持 Undo，不修改已有 EventSystem。场景中没有 EventSystem 时创建一个 legacy StandaloneInputModule；当前项目使用 legacy 输入。如果改用新 Input System，需要由项目配置合适的 UI Input Module。不要在运行时或 Play Mode 切换期间生成代码。

Canvas 默认使用 Screen Space Overlay、1920×1080、Scale With Screen Size，Main/Popup/Overlay 排序为 0/100/200，可在 Inspector 调整。全屏背景与现有 ScreenAdaptation 的 SafeArea 内容按原模块约定组合。

## 生成文件保护

- 只有带本生成器标记、且 namespace/window 身份一致的 WindowBase.g.cs 可以更新。
- 三个业务文件只在不存在时创建，重新生成不会写入已有文件。
- 修改命名空间不能在同一输出目录中自动迁移已有业务脚本；生成器拒绝覆盖身份不符的基类。
- 输出路径必须位于 Assets 内，不允许路径跳转或经过符号链接/目录联接。
- 生成基类固定管理 View 获取与释放，业务通过 OnInitialize、OnDispose 扩展，避免忘记调用 base 破坏生命周期。
- 生成器不会自动移动现有代码，也不会写入 Unity 生成的 csproj/sln。

## 使用示例

以下业务类型在使用生成器后存在。构造函数依赖可以通过注册工厂注入。

```csharp
using AshFramework.UI.UIModule;
using Game.UI.Inventory;

var registry = new UIRegistry();
InventoryWindowBase.Register(registry, () => new InventoryWindow());

// rootUI 为通过 Inspector 注入的 RootUI 引用，不依赖全局查找或单例。
IUIService ui = rootUI.CreateService(registry);

// 可选预加载，不显示。
await ui.LoadAsync(InventoryWindowBase.Key, cancellationToken);

// 第一次需要时也可以直接 Show，自动完成加载。
await ui.ShowAsync(
    InventoryWindowBase.Key,
    new InventoryArgs(),
    cancellationToken);

await ui.HideAsync(InventoryWindowBase.Key);
await ui.ShowAsync(InventoryWindowBase.Key, new InventoryArgs());
await ui.CloseAsync(InventoryWindowBase.Key);
```

注册始终显式传入工厂。需要构造函数注入时，在自己的 InventoryWindow 中新增构造函数，再使用 `InventoryWindowBase.Register(registry, () => new InventoryWindow(inventoryService))`，无需修改生成基类或保留额外无参构造。

业务文件示例：

```csharp
protected override void OnInitialize()
{
    // 绑定 View 的按钮和列表事件。
}

protected override void OnBind(InventoryArgs args)
{
    View.Render(args);
}

protected override void OnShow()
{
    // 仅显示期间需要的业务订阅。
}

protected override void OnHide()
{
    // VisibleToken 已取消，停止显示期间的订阅。
}

protected override void OnDispose()
{
    // 解除控件事件，允许清理部分初始化结果。
}
```

## 运行契约

- 构造服务及公共 API 必须在 Unity 主线程调用；取消通知自动投递到该线程。
- Registry 在创建服务后冻结；UIKey 的 ID 在该 Registry 内唯一且参数类型固定。
- 每个 key 一个实例；同 key 并发请求共享加载，参数按请求顺序执行 OnBind。
- 同层按 Show 请求序号排序，避免加载完成顺序改变遮挡关系。重复 Show 更新参数和顺序，不重复 OnShow。
- Hide 保留实例和租约；Close 取消待提交展示、隐藏并释放。加载中的 Close 等待迟到资源被清理。
- 单个请求的 token 取消不会取消其他等待者需要的共享加载；成功提交后取消不会追溯关闭窗口。
- 查询不加载。UIService.GetTop 可查询某层顶端，不代表页面导航或模态输入策略。
- 生命周期函数为同步通知，回调内新 UI 命令排队执行，不要使用同步等待阻塞这些异步命令。
- OnBind/OnCreate/OnShow 失败会清理失败实例；释放失败会保留 Closing 屏障，明确返回错误，后续显式 Close 可以重试未完成资源清理。
- Context.LifetimeToken 在关闭时取消；Context.VisibleToken 在隐藏时取消。重复绑定的数据异步响应由业务另用版本号或绑定取消源隔离。
- Context.CloseAsync 绑定实例身份，旧窗口的延迟回调不能关闭同 key 的新实例。
- RootUI 销毁时请求服务关闭全部实例；如果需要等待清理结束，先显式 await rootUI.Service.ShutdownAsync，再销毁根节点。

自定义加载器须在主线程返回未激活、带 RectTransform 和具体 UIView 的根节点，返回前的失败清理由加载器负责。成功返回后由服务独占租约，ReleaseAsync 必须幂等并支持重试未完成清理。Resources 适配器不会在每次关闭时触发全局 UnloadUnusedAssets。

## 验证

```powershell
python .codex/scripts/workflow.py verify --task csharp --include Assets/AshFramework/UI/UIModule
python .codex/scripts/workflow.py verify --task editor --test-filter AshFramework.UI.UIModule.Tests
```

测试覆盖显示与复用、共享加载、反序完成、取消、迟到实例、关闭重开、回调重入、资源释放重试、生成保护及路径校验。是否实际执行通过以当次 `.codex/runs/` 报告为准；代码编译与源码检查不能替代 Editor 测试、菜单操作和视觉验收。

测试用的 TestUIView 位于单独的 TestSupport 程序集，允许 Unity 将其挂载到 GameObject；不把 MonoBehaviour 放进仅 Editor 的测试程序集。该程序集使用 TestAssemblies 标记且不自动引用。

2026-10-17 原生界面验收通过：当前全部 22 个 EditMode 用例通过，0 失败、0 未运行；Hierarchy/Inspector 菜单、首次生成、连续生成保护业务文件、重编译后配置保留及 RootUI Undo 均已实操。临时样例已归档到忽略的验收证据目录。详情及验收边界见 [Unity 内验收记录](../../../../Docs/UIFramework-Acceptance-20261017.md)。
