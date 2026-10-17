# UI 框架初版设计 v0.1

状态：核心实现已按确认规则写入 Assets/AshFramework/UI/UIModule，本文保留设计伪代码用于说明协议；实际接口与操作以该模块 README 和源码为准。已完成补充编译，Unity Editor 测试与视觉验收尚未执行，不能据此宣称运行验证通过。

## 1. 目标与边界

核心职责是：按需加载 UI Prefab、创建窗口逻辑、显示与传参、隐藏复用、关闭释放、维护显示层级。

初版按 uGUI、单个 RootUI、每个 UIKey 一个窗口实例设计。不同 UIKey 可以绑定相同 Prefab。UIKey 是注册身份，资源地址只用于加载，窗口类型只用于创建逻辑对象，三者不混用。

初版不内置页面路由与返回栈、自动全屏互斥、模态遮罩、窗口多实例、MVVM、代码生成、通用对象池、动画系统、自动缓存淘汰和全局 Update 分发。需要模态输入或返回栈时，后续应增加明确策略；Popup 层本身不代表模态。

“缓存”仅表示隐藏后继续持有窗口实例和它的资源租约。`CloseAsync` 始终释放；由业务选择 Hide 或 Close，初版不额外引入关闭策略枚举。

## 2. 参考结论与项目约束

### 已核实的本地源码

| 来源 | 已有设计 | 本方案采用方式 |
| --- | --- | --- |
| RecyclerView-main / Framework/View/IView.cs、View.cs | OnCreate、OnStart、OnStop、OnDestroy；View 本身是 MonoBehaviour | 保留清晰的创建、展示、隐藏、释放周期；另设 OnBind 处理每次展示的数据 |
| RecyclerView-main / Framework/View/UIManager.cs | 字符串名称寻址、Open/Close、ViewType、Cache；TryGetView 会尝试创建 | 引入强类型参数的 UIKey；查询接口保持只读；Hide 和 Close 直接表达复用与释放 |
| RecyclerView-main / Framework/Load/ResourceManager.cs、Loader.cs | Addressables 按标签批量加载，窗口管理接收 Prefab 字典 | 提取加载边界，改为窗口首次需要时按地址加载，不要求启动时加载所有窗口 |
| RecyclerView-main / Framework/Widgets/RecyclerView | RecyclerView、Adapter、ViewHolder、ViewProvider 分工，Provider 管理列表项复用 | 列表属于窗口内部组件；窗口管理器不管理列表项 |
| TEngine / UIModule/IUIResourceLoader.cs | 加载器接口隔离具体资源模块，返回实例；采用 UniTask | 保留加载器适配边界；通过租约把实例与资源释放配对 |
| TEngine / UIModule/UIBase.cs、UIWindow.cs、UIWidget.cs | 窗口和 Widget 为普通 C# 对象；创建、刷新、可见性、销毁有独立回调 | 窗口逻辑采用普通 C# 对象；Prefab 上的 UIView 只保存展示引用与局部表现 |
| TEngine / UIModule/UIModule.cs、WindowAttribute.cs | 分层、全屏遮挡、隐藏后延迟关闭、多种同步/异步入口 | 初版保留分层和可等待的异步入口，其他策略按实际需求再加入 |

TEngine 参考路径中的 UIModule 位于 GameLogic 范围，不能当作独立可直接移入的 UI 包。其源码依赖 TEngine、资源模块、计时器、事件管理和 UniTask；本文仅提炼职责。

提供的[知乎文章](https://zhuanlan.zhihu.com/p/2045192611028276236)未能读取正文。网页工具访问失败，浏览器访问超时；随后使用 zhihu-reader 技能再次读取，公开请求返回 `not_authenticated`（退出码 4），消息为“获取文章详情失败: 获取文章详情: 请求参数异常，请升级客户端后重试。”这可能是 CLI/API 兼容性或站点限制，不能据此断定登录即可解决。按技能允许使用官方浏览器提取器重试一次，仍返回“官方浏览器提取器未能取得知乎登录状态；可能未登录、数据库锁定或浏览器加密不兼容。”本版不归纳文章观点，不宣称已经按文章内容对齐；后续获得正文后才能补充这部分比较。

### AshFramework 当前边界

- 当前项目版本为 Unity 2022.3.62f1c1，manifest 中存在 uGUI。
- Assets/AshFramework 下已确认的 UI 运行时模块是 ScreenAdaptation，拥有独立 Runtime/Editor 程序集；本方案不把屏幕适配计算并入窗口管理。
- 现有 Windowing/Editor/PopupWindowEditorWindow.cs 是 EditorWindow，不是游戏运行时弹窗基类。
- 当前 Assets、Packages 清单未确认 UniTask、Addressables 或 YooAsset 可用，不能根据残留的生成 csproj 推定依赖已经安装。因此伪代码先用标准 Task；资源实现通过适配器接入。本文不安装依赖。
- 所有实例化、激活、层级调整、生命周期回调与释放中的 Unity 操作均在主线程执行。不用 Task.Run 包裹 Unity API。

## 3. 核心结构

```text
业务调用
    |
    v
IUIService / UIService
    |-- UIRegistry：UIKey -> 定义 + 窗口工厂
    |-- IUIViewLoader：异步创建实例，返回资源租约
    |-- UILayerManager：维护每层可见窗口及请求顺序
    |-- RootUI：挂载、Canvas 配置、可见性与输入
    `-- UIWindow<TArgs>：窗口逻辑与生命周期
            `-- UIView：Prefab 引用、局部渲染与控件事件
```

服务不依赖业务窗口类，不直接调用任何具体资源系统；Registry 从游戏启动层显式注册。先不增加 UIController、UIModel、UIPresenter 等额外层级，窗口可以通过构造函数接收业务依赖。

RootUI 是具体的 MonoBehaviour，不要求为每个内部类都定义接口。当前只给外部入口和资源适配保留接口。

## 4. 对外接口

以下属性用于描述协议，省略构造函数、参数校验、内部状态与通用实现。

```csharp
public readonly struct UIKey<TArgs>
{
    public string Id { get; }
}

public readonly struct NoArgs
{
    public static NoArgs Value { get; }
}

public enum UILayer
{
    Main,
    Popup,
    Overlay
}

public enum UIState
{
    Loading,
    Hidden,
    Showing,
    Visible,
    Hiding,
    Closing,
    Closed
}

public sealed class UIDefinition
{
    public string AssetKey { get; }
    public UILayer Layer { get; }
}

public interface IUIHandle
{
    string Id { get; }
    UIState State { get; }
}

public interface IUIService
{
    Task<IUIHandle> LoadAsync<TArgs>(
        UIKey<TArgs> key,
        CancellationToken cancellationToken = default);

    Task<IUIHandle> ShowAsync<TArgs>(
        UIKey<TArgs> key,
        TArgs args,
        CancellationToken cancellationToken = default);

    Task HideAsync<TArgs>(UIKey<TArgs> key);

    Task CloseAsync<TArgs>(UIKey<TArgs> key);

    bool TryGet<TArgs>(UIKey<TArgs> key, out IUIHandle handle);
}

public sealed class UIRegistry
{
    public void Register<TArgs>(
        UIKey<TArgs> key,
        UIDefinition definition,
        Func<UIWindow<TArgs>> factory)
    {
        // 检查空 ID、重复 ID、地址与工厂，存储类型擦除后的记录。
        // 同一 ID 只能注册一次；TArgs 类型不匹配时在入口立即报错。
    }
}
```

| 操作 | 承诺 |
| --- | --- |
| LoadAsync | 首次调用加载 Prefab、创建逻辑并执行 OnCreate，完成后保持 Hidden；已有实例时直接返回，不改变可见性 |
| ShowAsync | 自动确保实例就绪，执行 OnBind(args)，必要时激活并执行 OnShow，按此次 Show 请求序号确定同层顺序；完成时为 Visible |
| HideAsync | 已创建实例隐藏后保留，完成时为 Hidden；加载中则撤销此前 Show 意图，不制造一个本来无人需要的缓存实例 |
| CloseAsync | 撤销该窗口未完成的加载/展示，隐藏、释放逻辑和租约；完成后不再登记该实例 |
| TryGet | 仅查询已经完成 OnCreate 的现存实例，包括 Hidden；不加载、不创建、不等待；Loading/Closing/Closed 返回 false |

未登记实例的 Hide/Close 是幂等空操作。Show/Load 使用未注册 key 时明确失败，不静默忽略。IUIHandle 是单次实例的只读观察对象，关闭后保持 Closed，重开时产生新句柄；关闭命令按 key 作用于当前实例。

本版不提供 GetAsync 轮询、“异步但返回 void”的打开接口或同步资源加载接口。调用方通过 await 获得确定的完成或失败结果。

## 5. 加载与释放契约

```csharp
public interface IUIViewLoader
{
    Task<IUIViewLease> InstantiateAsync(
        string assetKey,
        Transform inactiveStagingRoot,
        CancellationToken cancellationToken);
}

public interface IUIViewLease
{
    GameObject Instance { get; }

    Task ReleaseAsync();
}
```

`IUIViewLoader` 同时承担资源加载和实例化。创建过程放在 inactiveStagingRoot 下，成功返回时实例自身也必须为 inactive，防止挂到活动父节点时提前启用。UIService 校验根节点具有 RectTransform 和 UIView，再挂载到目标层。

租约的唯一所有者是 UIService。适配器定义内部如何释放，UIService 不再额外 Destroy 同一个实例：

1. 成功返回租约：所有权转给 UIService，窗口 Hide 时继续持有，Close 时仅释放一次。
2. 返回租约前失败或取消：加载器负责清理自己已经获得的资源与实例。
3. 加载不支持真正取消：允许后台完成，但失效结果必须立即释放，禁止挂到显示层。
4. 使用 LoadAsset + Instantiate 的适配器，释放时必须先完成实例销毁，再释放 Prefab 资源引用，避免 Destroy 延迟导致引用提前卸载。
5. 使用资源系统实例化句柄的适配器，按其配对协议释放，避免重复销毁或重复释放。

`ReleaseAsync` 必须可重复调用且不重复释放；完成表示这份租约不再持有实例与资源引用，不保证底层系统立刻回收全部物理内存。

初版可以编写 Resources 适配器。它的释放表示销毁实例和移除本地引用，不在每次 Close 时调用全局 UnloadUnusedAssets。后续接入其他资源系统时，UIService 和业务调用不变。

## 6. 窗口生命周期

```csharp
public abstract class UIView : MonoBehaviour
{
    // Prefab 上的引用与局部表现，不负责全局打开、关闭和资源释放。
}

public abstract class UIWindow<TArgs>
{
    // 以下回调由 UIService 通过内部桥接调用，业务不直接调用。

    protected virtual void OnCreate(UIView view)
    {
    }

    protected virtual void OnBind(TArgs args)
    {
    }

    protected virtual void OnShow()
    {
    }

    protected virtual void OnHide()
    {
    }

    protected virtual void OnRelease()
    {
    }
}
```

| 回调 | 次数与职责 |
| --- | --- |
| OnCreate | 每个实例一次；校验并缓存 UIView 类型、绑定控件事件、创建局部组件；此时根节点未激活 |
| OnBind | 每次被执行的 Show 意图一次，包括已显示窗口；使用此次参数刷新数据 |
| OnShow | 每次 Hidden -> Visible 一次；节点已激活，开始显示期间的业务订阅 |
| OnHide | 每次 Visible -> Hidden 一次；取消显示期间任务和订阅，随后禁用节点 |
| OnRelease | 实例最终释放一次；解除控件事件、释放局部对象；节点与租约由框架随后释放 |

```text
首次显示：创建租约 -> OnCreate -> OnBind -> 激活 -> OnShow
预加载：  创建租约 -> OnCreate -> Hidden
重复显示：Visible -> OnBind -> 同层置顶
隐藏复用：OnHide -> 禁用 -> Hidden -> OnBind -> 激活 -> OnShow
关闭显示：OnHide -> 禁用 -> OnRelease -> ReleaseAsync -> Closed
关闭隐藏：OnRelease -> ReleaseAsync -> Closed
```

这些回调初版都是同步通知，避免“框架等待窗口 OnShow，而 OnShow 又等待自身 Close”的循环等待。网络请求由业务在展示前获取，或在展示后作为可取消任务运行，不阻塞窗口事务。需要异步入场动画时再单独增加可等待过渡接口。

OnCreate 中途失败也要进入清理路径；OnRelease 应能清理部分初始化结果。某个清理回调抛出异常不能阻止后续租约释放。失败会从当前公共异步调用返回，并携带 key、assetKey 和操作阶段；不返回伪成功句柄。

## 7. RootUI 与显示规则

初版每层一个 Canvas，以层根 sibling 顺序管理窗口；不要求每个窗口自带独立排序 Canvas。

```text
RootUI
|-- MainCanvas       [Canvas / CanvasScaler / GraphicRaycaster]
|   |-- MainWindowA
|   `-- MainWindowB
|-- PopupCanvas      [Canvas / CanvasScaler / GraphicRaycaster]
|   `-- PopupWindowA
|-- OverlayCanvas    [Canvas / CanvasScaler / GraphicRaycaster]
|   `-- LoadingWindow
`-- StagingRoot      [inactive]

EventSystem          [场景中一个有效实例]
```

各层采用相同的 CanvasScaler 配置，Canvas 排序满足 Main < Popup < Overlay。UIService 负责挂到对应层，UILayerManager 管理层内顺序。Prefab 内部子 Canvas 初版禁止独立 overrideSorting，防止越过窗口和层的排序边界；以后确有需求再定义局部排序协议。

UILayerManager 使用 `Dictionary<UILayer, List<WindowEntry>>`，仅登记可见实例，每个列表按实例最近一次成功 Show 的请求序号升序排列，末尾是本层顶端。初版允许 O(n) 插入和 sibling 重排；窗口数量较少，先保持规则简单。它不保存参数，也不承担加载、释放或页面回退。

每次接受 Show 时，UIService 分配全局递增 `ShowSequence`；提交成功时才把该序号写入窗口记录并排序。先请求 A、后请求 B，即使 A 后加载完成，A 仍位于 B 下方。不同 key 独立加载，无需等待先请求窗口才能显示后请求窗口。重复 Show 已有窗口会获得新的序号，实现同层置顶；取消或失败的请求不改变原有顺序。

Hide/Close 从可见列表移除实例，其他窗口相对顺序不变。每层仅一套排序真相，不再把 sibling 顺序反读成业务状态。这里是显示顺序表；当前不提供导航栈，不自动恢复被业务隐藏的上一页。

窗口隐藏时根 GameObject 禁用，框架也应在切换前清除其后代上的 EventSystem 当前选中对象，防止键盘/手柄继续作用于隐藏窗口。Show 不自动把键盘焦点给某个按钮，后续可增加独立焦点策略。

全屏背景放在窗口根下，交互内容放在带现有 SafeArea 的 Content 节点下。不要把整个 Canvas 或背景都缩进安全区。CanvasWidthAdjuster 按现有模块约定配置；核心服务不写入屏幕适配算法，不把适配服务的暂停/恢复当作窗口生命周期。

## 8. 并发、取消与失败

窗口记录内部至少包含：key、generation、state、sharedLoadTask、待处理命令、租约、逻辑对象和只读句柄。记录按 key 管理，generation 用于阻止旧加载结果影响重开的窗口。

| 场景 | 初版规则 |
| --- | --- |
| 相同 key 同时 Load/Show | 共享一份加载和一份实例；每个调用保有独立等待结果 |
| 不同 key 并发 Show | 独立加载；用请求时分配的 ShowSequence 排序，不用加载完成时间排序 |
| 加载完成前多次 Show，参数不同 | 按主线程收到命令的顺序执行 OnBind，最后一个参数成为最终内容；调用方必须传参数快照，不依赖之后修改的可变对象 |
| Show 已经 Visible 的窗口 | 执行 OnBind、同层置顶，不重复执行 OnShow |
| Hide 发生在加载中 | 撤销此前尚未执行的 Show 意图；这些 Show 以取消结束；已有 Load 请求可完成并保持 Hidden，无人需要时撤销加载 |
| Close 发生在加载中 | 立即使当前 generation 失效并取消待执行请求；迟到的租约只走释放路径，Close 等待这些资源清理完毕 |
| Close 后又 Show | 新 Show 等旧实例完成关闭，再创建新 generation；旧结果不能注册到新实例 |
| 单个等待者取消 | 仅撤销该等待者尚未提交的请求；不能把其他等待者需要的共享加载取消 |
| 所有加载等待者都取消 | 若没有已提交窗口状态或其他保留意图，撤销内部加载；不支持中断时等待结果后释放 |
| Show 已提交后调用方取消 | 不追溯关闭窗口；显示存在是 UIService 的状态，不由已完成调用的 token 所有 |
| 资源不存在、View 类型错误、回调失败 | 明确报错、清理失败实例、移除登记，后续允许重新加载 |
| Hide/Close 重复调用 | 状态不重复迁移，生命周期和释放最多执行对应次数；并发 Close 共享关闭完成结果 |

所有公共命令、取消通知和异步完成通知都交给主线程调度；生命周期回调内发起的新 UI 命令先入队，在当前同步提交结束后处理，不能递归修改正在遍历的窗口集合。同 key 的待展示请求按接收顺序提交，不同 key 的加载可以并行。Close/Hide 可以在资源等待阶段撤销尚未提交的 Show，不必等普通展示队列轮到它们。

取消判断与命令提交在主线程上确定先后。不要把首个调用方的 token 直接用作整个共享加载的 token。短暂状态变更可以串行处理，但不能持有阻止 Close 的锁等待整段资源加载；加载完成后重新检查 generation 才能提交。

`LoadAsync`/`ShowAsync` 的成功状态以该命令的提交点为准。此后新命令可以继续改变窗口状态；先前返回的句柄是实时观察对象，不是永久 Visible 的保证。

实现阶段建议为命令队列补专项测试，初版无需在业务使用层暴露 generation、锁和资源句柄。

## 9. 注册与使用示例

以下 InventoryArgs、InventoryView、InventoryWindow、ResourcesUIViewLoader、RootUI 和 UIService 均为拟议类型。InventoryView.Render 表示业务自己的渲染方法，未声称当前项目或参考库存在该 API。

### 定义参数和窗口

```csharp
public sealed class InventoryArgs
{
    public int TabId { get; }
}

public static class UIKeys
{
    public static readonly UIKey<InventoryArgs> Inventory =
        new UIKey<InventoryArgs>("Inventory");

    public static readonly UIKey<NoArgs> Loading =
        new UIKey<NoArgs>("Loading");
}

public sealed class InventoryWindow : UIWindow<InventoryArgs>
{
    private InventoryView _view;

    protected override void OnCreate(UIView view)
    {
        _view = RequireView<InventoryView>(view);
        // RequireView 为拟议的基类辅助方法，类型不匹配时抛出带窗口信息的错误。
        // 在这里绑定控件事件，每个实例一次。
    }

    protected override void OnBind(InventoryArgs args)
    {
        _view.Render(args.TabId);
    }

    protected override void OnHide()
    {
        // 停止显示期间的订阅、计时器和异步任务。
    }

    protected override void OnRelease()
    {
        // 解除 OnCreate 中的事件，释放窗口自己的局部资源。
        _view = null;
    }
}
```

### 启动注册

```csharp
var registry = new UIRegistry();

registry.Register(
    UIKeys.Inventory,
    new UIDefinition("UI/Inventory", UILayer.Main),
    () => new InventoryWindow());

registry.Register(
    UIKeys.Loading,
    new UIDefinition("UI/Loading", UILayer.Overlay),
    () => new LoadingWindow());

IUIViewLoader loader = new ResourcesUIViewLoader();
IUIService ui = new UIService(registry, loader, uiRoot);
```

显式工厂便于构造函数注入和热更新环境接入，不要求反射扫描程序集或通过类名推导资源地址。使用 Resources 适配器时，示例资源地址对应 Resources 下的相对路径；其他适配器使用自己的地址规则。

### 业务调用

```csharp
// 可选预加载：创建并初始化，保持隐藏。
await ui.LoadAsync(UIKeys.Inventory, cancellationToken);

// 首次显示；也可以省略预加载直接 Show。
await ui.ShowAsync(
    UIKeys.Inventory,
    new InventoryArgs(tabId: 0),
    cancellationToken);

// 已显示时刷新参数并置顶，不创建第二个实例。
await ui.ShowAsync(
    UIKeys.Inventory,
    new InventoryArgs(tabId: 2),
    cancellationToken);

// 保留实例与资源，下次显示继续复用。
await ui.HideAsync(UIKeys.Inventory);

// 只查询状态，不引发加载。
if (ui.TryGet(UIKeys.Inventory, out IUIHandle handle))
{
    Debug.Log(handle.State);
}

// 关闭并释放；之后再 Show 会重新创建。
await ui.CloseAsync(UIKeys.Inventory);

// 无参数窗口也使用同一套接口。
await ui.ShowAsync(UIKeys.Loading, NoArgs.Value, cancellationToken);

try
{
    await LoadBusinessDataAsync(cancellationToken);
}
finally
{
    await ui.CloseAsync(UIKeys.Loading);
}
```

Loading 示例假设该业务流程独占此 key。多个并行业务共用 Loading 的计数管理应由业务层单独封装，初版 UIService 不自动引用计数“打开次数”。

列表方面，InventoryView 可以组合 RecyclerView；它自行创建 Adapter、选择 LayoutManager 并提交数据。窗口隐藏和释放时由窗口/局部组件清理对应订阅。不要让每个 ViewHolder 都成为全局窗口，也不要再给 RecyclerView 的 ViewProvider 叠加一层窗口对象池。此处仅保留接入位置，不增加第二套列表接口。

## 10. 拟议目录与后续验收

```text
Assets/AshFramework/UI/UIModule/
|-- Runtime/
|   |-- Contracts/       IUIService、IUIHandle、UIKey、UIState
|   |-- Loading/         IUIViewLoader、IUIViewLease、Resources 适配器
|   |-- Core/            UIService、UIRegistry、UILayerManager、窗口记录与请求
|   |-- Views/           UIView、UIWindow<TArgs>
|   |-- Components/      RootUI
|   `-- AshFramework.UI.UIModule.asmdef
|-- Editor/              RootUI 创建、Inspector、Window 基类生成器
`-- Tests/Editor/        请求生命周期和生成保护测试
```

Runtime 计划只依赖 Unity 运行时、uGUI 和标准库，不引用 UnityEditor、GameLogic、TEngine 或 RecyclerView。UIWindow/UIView 基类与 UIService 初版放在同一运行时程序集，避免过早拆出多个 Contracts 包。游戏层注册业务窗口；屏幕适配组件由游戏 Prefab 组合，两模块不互相依赖。

若后续确需 UniTask 或专门资源系统，需先确认实际包与程序集名，再决定独立适配程序集；不把未安装 API 写成现成能力。

实现阶段至少验证以下行为：

1. Load 不显示，Show 到 Visible 才完成；Hide 后复用，Close 后重新创建。
2. 同 key 并发只创建一个实例，不同 key 互不串扰，参数顺序符合协议。
3. 加载中 Hide、Close、取消，以及 Close 后重开不会出现迟到弹窗或资源泄漏。
4. 一个等待者取消不影响其他等待者；资源及回调失败可以清理并重试。
5. 查询没有创建副作用，重复 Hide/Close 不重复触发生命周期与释放。
6. 层排序、隐藏后鼠标与键盘输入、全屏背景和安全区内容通过 Editor 与视觉验收。
7. 不同窗口反序加载完成、回调内重入 UI 操作、关闭后旧业务请求返回时，顺序与实例隔离仍然成立。

核心运行时、Editor 扩展、程序集定义与相关测试源码现已创建；没有直接生成业务窗口、Prefab 或修改现有场景。以上用例仍需在本项目 Editor 中执行。

已确认业务脚本默认目录为 Assets/GameScripts/UI/<窗口名>/，Generated/<窗口名>WindowBase.g.cs 可更新，Window.cs / Args.cs / View.cs 只在首次创建时生成；默认计划 Prefab 位于 Assets/Resources/UI/<窗口名>.prefab。生成时可以选择脚本目录、命名空间及其他 Resources 子目录。现有 Windowing 编辑器工具保持原位。详细使用见 [UIModule README](../Assets/AshFramework/UI/UIModule/README.md)。

## 11. 内部实现结构与数据所有权

核心只保留一份实例登记、一份分层顺序表。其余是请求的临时数据和窗口自己的业务状态。

| 数据 | 所有者 | 内容与生命周期 |
| --- | --- | --- |
| definitions | UIRegistry | ID -> 参数类型、资源地址、层级、工厂；启动注册后冻结，业务服务启动前完成校验 |
| entries | UIService | ID -> 当前 WindowEntry；Loading 到 Closing 都占有槽位，完成释放后才移除 |
| pending requests | UIService | key、参数快照、请求序号、取消登记、完成源；成功、失败或取消后移除并释放参数引用 |
| visibleLayers | UILayerManager | 每层当前可见实例；只引用 entries 中尚未关闭的记录，不拥有资源 |
| lease | WindowEntry / UIService | 唯一实例租约；加载器成功返回前由加载器负责，返回后由 UIService 接管 |
| display args / business data | UIWindow 与业务层 | UIWindow 在 OnBind 中消费参数，可按需要保存；框架不建立永久参数历史或业务模型缓存 |
| controls / local components | UIView | 序列化控件引用、局部渲染与事件；不引用内部窗口字典或资源句柄 |

拟议内部数据如下。IWindowDriver 是 UIWindow<TArgs> 的非泛型内部桥接，负责经校验后调用泛型生命周期；公共 API 不暴露 object[]。

```csharp
internal sealed class WindowEntry
{
    public string Id;
    public long Generation;
    public bool Retired;
    public UIState State;
    public UILayer Layer;
    public long ShowSequence;
    public IUIViewLease Lease;
    public IWindowDriver Driver;
    public IUIHandle Handle;
    public Task LoadTask;
    public Task CloseTask;
    public CancellationTokenSource LoadCancellation;
    public CancellationTokenSource LifetimeCancellation;
    public CancellationTokenSource VisibleCancellation;
}

internal sealed class ShowRequest<TArgs>
{
    public UIKey<TArgs> Key;
    public TArgs Args;
    public long Sequence;
    public CancellationToken Cancellation;
    public TaskCompletionSource<IUIHandle> Completion;
}
```

Generation 为每个实例分配，不在实例生存期间复用。Retired 在关闭或废弃时设置；异步结果提交前必须同时核对“entries 仍指向此记录”和“未 Retired”。关闭期间保留该记录作为释放屏障，新 Show 等待 CloseTask 完成后重新解析当前槽位，不能提前创建替代实例。

窗口不获得可写的 WindowEntry。UIService 内部注入一个实例上下文，提供生命周期 token、显示周期 token 和绑定当前记录的关闭请求。内部关闭请求先验证实例身份；旧窗口的延迟回调不能关闭同 key 的新窗口。按 key 的公共 Close 则有意作用于当前窗口。

LifetimeCancellation 在 Close 时取消；VisibleCancellation 在每次开始显示时创建，在 Hide/Close 时取消。重复 Show 已显示窗口仍在同一显示周期；需要防止旧参数的数据响应覆盖新参数时，窗口另用 BindVersion 或每次绑定的取消源处理。取消回调与 token 释放不应在持有状态锁时执行。

## 12. 完整操作流转

### 12.1 启动注册

```text
游戏启动代码
  -> 构造 Registry 并注册 UIKey / 定义 / 窗口工厂
  -> 校验并冻结 Registry
  -> 注入 RootUI、加载器、Registry，创建 UIService
  -> 业务通过 IUIService 发起请求
```

### 12.2 首次显示

```text
ShowAsync(key, args, token)
  -> 主线程校验 key、参数类型、取消状态
  -> 分配 ShowSequence，建立带参数的请求
  -> 获取或创建 WindowEntry，分配 Generation
  -> 共享 LoadTask，状态 Loading
  -> Loader 在暂存根下创建 inactive 实例，返回租约
  -> 核对记录身份和 Retired，失效结果立即释放
  -> 校验 RectTransform / UIView，调用工厂创建 UIWindow
  -> 注入实例上下文，执行 OnCreate，进入 Hidden
  -> 按本 key 请求顺序取出尚未取消的 Show
  -> OnBind(args)，在 inactive 状态下填充初始内容
  -> 挂到目标层、创建显示周期、进入 Showing
  -> 激活并执行 OnShow
  -> 成功后按请求序号提交可见列表和 sibling 顺序
  -> 状态 Visible，完成本次请求并清除请求持有的参数
```

RootUI 的挂载、激活与同一次层级提交在一个不含 await 的主线程步骤内完成。完成 Show 表示生命周期和显示状态已提交，不保证下一帧布局、远端图片和异步业务数据已经完成；调用方需要它们时使用独立的业务完成条件。

只有 LoadAsync 时，流程止于 Hidden，不执行 OnBind、OnShow，也不加入 visibleLayers。已有 Visible 实例的 Load 不触发隐藏。

### 12.3 已显示窗口刷新

```text
ShowAsync(同 key, 新 args)
  -> 复用当前实例，按同 key 顺序执行 OnBind
  -> 成功后更新 ShowSequence、重排本层顺序
  -> 保持 Visible，不重复 OnCreate / OnShow
```

OnBind 若抛错，初版选择关闭并清理当前失败实例，通知未完成的相关请求失败；不尝试回滚任意业务字段。之前已经完成的 Show 请求不追溯改成失败。

### 12.4 隐藏与重新显示

```text
HideAsync(key)
  -> 撤销此前尚未提交的 Show
  -> Visible -> Hiding
  -> 取消显示周期任务、清除该窗口后代的选中对象
  -> OnHide，禁用节点，移出 visibleLayers
  -> Hidden；entries、Driver、UIView、租约保持存在

之后 ShowAsync(key, args)
  -> OnBind -> 新显示周期 -> 激活 -> OnShow
  -> 按新请求序号加入本层 -> Visible
```

加载中 Hide 不调用尚未创建窗口的生命周期。若还有有效 Load 请求，就在加载完成后保留 Hidden；否则撤销内部加载，并清理迟到结果。OnHide 异常时也要先禁用并移出显示列表，再走关闭清理并返回异常，不能留下仍能输入的半隐藏窗口。

### 12.5 关闭与加载中关闭

```text
CloseAsync(key)
  -> Retired = true，状态 Closing，建立或复用 CloseTask
  -> 撤销待提交请求，取消加载、显示周期和实例周期任务
  -> 如已进入显示周期，执行一次 OnHide，禁用并移出显示列表
  -> 如已开始初始化逻辑，执行一次 OnRelease
  -> 等待未完成加载；迟到租约只清理，不接入场景显示层
  -> ReleaseAsync，释放实例及关联资源引用
  -> 清除业务、View、参数和取消登记等引用
  -> 句柄 Closed，移除当前记录，完成 CloseTask
```

释放阶段各清理步骤用独立的异常收集和 finally 保证继续执行；最终把错误返回给 Close 的调用者。若底层释放失败且资源所有权仍未解决，保留 Closing 记录并返回失败，阻止同 key 重开；只有后续确认清理完成才移除。不能在释放失败时伪造 Closed。并发 Close 共享正在执行的 CloseTask；已失败的关闭任务不永久复用，后续显式 Close 仅重试未完成的资源清理，不重复已完成的生命周期。此时 Show/Load 明确返回清理失败，不无限等待或自行绕过释放屏障。租约应记录各释放阶段，使重复调用能继续未完成步骤。

OnShow 中途失败也按进入过显示周期处理，补偿执行 OnHide，再释放。回调内新发出的命令只入队，不能在当前步骤中递归运行。

## 13. 业务数据与控件事件流转

框架的请求参数流与业务数据流是两条相关的路径：

```text
展示入口：业务入口 -> UIKey + Args -> UIService -> UIWindow.OnBind
                                         -> UIView.Render -> 控件/列表

用户操作：Button / RecyclerView 点击
          -> UIView 的业务事件
          -> UIWindow 的事件处理
          -> 注入的业务服务读取或修改 Model
          -> 返回结果 / 模型变更通知
          -> UIWindow 校验实例、显示周期、BindVersion
          -> 主线程 UIView.Render
```

UIService 只理解身份、资源、参数类型、生命周期和排序，不读取背包条目或拼接业务网络请求。Model 保留在游戏业务层，UIView 不承担业务数据的最终存储。OnCreate 绑定一次控件事件，OnRelease 解除；仅显示时需要的业务订阅在 OnShow 开始、OnHide 结束。

例如连续显示背包 Tab 0 和 Tab 2，即使 Tab 0 的请求最后返回，也必须通过 BindVersion 检查丢弃旧响应。窗口关闭重开时还必须通过实例生存期检查，不能把旧响应写入同 key 的新实例。取消 token 是尽力终止，提交前的身份与版本检查仍需保留。

RecyclerView 在局部消费条目集合：窗口/业务服务提供数据，Adapter 提交和更新列表，ViewHolder 绑定条目；列表项分配回收由 ViewProvider 负责。列表项点击沿 UIView 事件返回窗口，再由窗口决定业务操作或请求显示其他 UI。
