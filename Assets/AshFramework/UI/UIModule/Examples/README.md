# 最小 UI 层内栈示例

两个窗口共用 `UILayer.Popup`，通过现有 `UIService` 完成 Resources 加载、参数绑定、显示置顶、隐藏复用和关闭释放。左侧操作面板位于 Main 层，右侧窗口使用错位布局，可直接观察遮挡顺序。

## 运行

1. 打开 `Scenes/StackDemo.unity`，或执行 `Tools > AshFramework > UIModule > Examples > 3. Open Stack Demo`。
2. 进入 Play Mode，示例自动依次显示 A、B，B 位于最上面。
3. 点击左侧按钮，观察 `POPUP TOP`、窗口状态，以及 `Create / Bind / Release` 累计次数。

| 操作 | 行为 |
| --- | --- |
| Show A / Show B | 首次加载；已有实例重新绑定参数，并移到当前层顶部 |
| Hide top | 隐藏当前可见栈顶，保留实例，露出下一可见窗口 |
| Close top | 关闭并释放当前栈顶，再次 Show 时创建新实例 |
| Reset | 先关闭两个窗口，再依次打开 A、B；累计计数保留 |
| 窗口内部 Hide / Close | 通过该 Window 的实例上下文操作自身 |

示例展示的是已确认的“层内显示顺序”。同层窗口可以同时可见，显示上层不会自动隐藏下层；未增加导航返回历史、模态遮罩或输入独占策略。

## 目录与职责

```text
Examples/
|-- Runtime/
|   |-- StackExampleController.cs       场景入口、注册工厂、面板操作与状态展示
|   |-- StackPanelView.cs               两个 View 共用的序列化控件引用
|   |-- StackWindowStatistics.cs        注入 Window 的生命周期计数模型
|   |-- StackExampleActions.cs          Unity 按钮异步事件的异常处理
|   `-- Windows/
|       |-- StackFirst/
|       |   |-- Generated/StackFirstWindowBase.g.cs
|       |   |-- StackFirstWindow.cs
|       |   |-- StackFirstArgs.cs
|       |   `-- StackFirstView.cs
|       `-- StackSecond/                对应 B 的同样四个文件
|-- Resources/UIModuleExamples/StackDemo/
|   |-- StackFirst.prefab
|   `-- StackSecond.prefab
|-- Scenes/StackDemo.unity
|-- Editor/
|   |-- StackExampleScaffolder.cs        调用正式 Window 生成器
|   `-- StackExampleAssetBuilder.cs     使用 Unity Editor API 创建并连接资源
|-- Tests/PlayMode/                     真实 Resources 与射线交互集成测试
`-- Typography/TextMesh Pro/            官方 TMP Essentials 与许可证
```

示例业务命名空间是 `AshFramework.UI.UIModule.Examples.StackDemo`。Runtime、Editor、PlayModeTests 使用独立 asmdef，引用现有框架程序集；示例 Runtime 不自动引用到其他业务程序集。程序集定义显式引用 uGUI 与 TextMeshPro。

## 标准开发步骤

1. **生成骨架**：本示例实际执行过菜单 `1. Generate Window Scripts`，内部调用已有 `UIWindowCodeGenerator.Generate`，首次生成两个窗口共八个 C# 文件。输出目录和命名空间通过生成选项指定到本 Examples 中。
2. **实现业务**：维护 Args、View、Window 三个文件。Args 携带显示参数；View 渲染文本并持有控件引用；Window 在 `OnInitialize` 注册按钮，在 `OnBind` 更新 View，在 `OnDispose` 移除按钮监听。
3. **制作资源**：将具体 View 挂到对应 Prefab 根节点，连接文本与按钮。场景使用正式 `RootUIFactory` 创建 Main / Popup / Overlay Canvas 和 StagingRoot。本示例已使用 Editor API 创建并保存场景及 Prefab，可直接在 Unity Inspector 中继续编辑。
4. **注册与注入**：场景入口注册两个显式工厂，将计数模型与刷新回调传入 Window，然后调用 `RootUI.CreateService(registry)`，使用默认 `ResourcesUIViewLoader`。
5. **运行与验收**：执行 C# 工作流、PlayMode 集成测试，并在 Game 视图点击检查层级、文本、按钮和计数。

`Generated/*.g.cs` 保持生成器输出，不手写修改。重新执行步骤 1 时，只更新生成基类，已有 Window / Args / View 业务文件保留。

步骤 2 菜单 `2. Create Scene And Prefabs` 是首次搭建辅助工具。已提供示例资源后无需再次执行；任何目标场景或 Prefab 已存在时会拒绝覆盖，后续修改应在原资源上完成。创建前应保存已有未命名场景；工具会保留已有命名场景。若项目尚无 TMP Essentials，工具使用官方导入入口异步导入，需要等导入完成后再次执行步骤 2。

当前示例使用随资源提供的 LiberationSans SDF 静态字体、关闭 Auto Size，界面文案为英文。需要中文时，应按项目字体方案配置包含中文字形的 TMP 字体及 fallback，并在目标分辨率复验。

## 数据流转

```text
场景 Start
  -> UIRegistry 注册 Window 工厂（注入统计模型）
  -> RootUI.CreateService
  -> ShowAsync(Key, Args)
  -> ResourcesUIViewLoader 按 Resources 相对地址加载并实例化 Prefab
  -> Window.OnInitialize 绑定按钮
  -> Window.OnBind(Args) -> View.Render(Args)
  -> UIService 更新 Popup 层顺序、激活实例
  -> 面板刷新 GetTop / TryGet / 生命周期计数

面板按钮 -> Controller -> UIService
窗口按钮 -> Window -> Context.UI -> UIService
Hide -> 实例保留，移出可见顺序 -> 后续 Show 复用并置顶
Close -> OnDispose 移除监听 -> 租约销毁实例 -> 后续 Show 重新创建
```

Window 不直接维护 Transform 排序，也不引用场景控制器；层内顺序由框架维护。统计模型记录实际创建、绑定和释放次数，便于分辨隐藏复用与关闭重建。

## 验证记录（2026-10-17）

目标工程为 `D:/UnityProject/AshFramework`，Unity `2022.3.62f1c1`。

| 项目 | 结果与范围 |
| --- | --- |
| 标准 Window 生成 | Unity 原生菜单实际生成八个文件；生成基类的 SHA-256 与首次输出一致 |
| C# 工作流 | passed；15 个 C# 文件的块布局、解决方案构建、限定范围 whitespace 与大括号检查 |
| 原生 PlayMode Test Runner | 发现 2、通过 2、失败 0、未运行 0 |
| 原生 EditMode Test Runner | 发现 22、通过 22、失败 0、未运行 0；现有框架回归 |
| Game 视图 | 1920×1080 目标尺寸；检查初始 B 置顶、重复 Show A、Hide A 复用、Close A 释放、重开 A 创建次数增加、窗口自身按钮和 Reset |
| 工作流 Editor / Pipeline | blocked：工程没有可用 Pipeline 连接，未通过该通道运行编译或测试；与上面的原生测试分开记录 |

两项 PlayMode 测试覆盖真实 Resources 加载、实例复用与销毁、句柄更替、同层 sibling 顺序，以及重叠区域 GraphicRaycaster 命中顶层窗口、窗口按钮触发隐藏与关闭。

可重复运行的命令：

```powershell
python .codex/scripts/workflow.py --project D:/UnityProject/AshFramework verify --task csharp --include Assets/AshFramework/UI/UIModule/Examples
python .codex/scripts/workflow.py --project D:/UnityProject/AshFramework verify --task editor --test-filter AshFramework.UI.UIModule.Examples.Tests --test-mode playmode
```

无 Pipeline 时可在 Unity 原生 Test Runner 选择 PlayMode，运行 `AshFramework.UI.UIModule.Examples.PlayModeTests.dll`；框架回归在 EditMode 中选择 `AshFramework.UI.UIModule.Editor.Tests.dll`。本次命令日志、截图和综合报告存放于项目忽略目录 `.codex/runs/ui-stack-example-20261017/`，C# 工作流报告在 `.codex/runs/20261017T082935Z-f529b32c/`。

Console 中仍保留搭建阶段已经修复的两条历史工具异常，以及现有 HierarchyHelper 的 hook 警告；未将 Console 描述为完全无历史错误。两项新增集成测试和本次运行交互均已完成。
