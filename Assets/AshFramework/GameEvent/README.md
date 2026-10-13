# AshFramework 事件桥

运行时命名空间是 `AshFramework.Events`，Editor 命名空间是 `AshFramework.Events.Editor`。

## 使用流程

1. 在运行时程序集内定义 public 事件接口，添加 `[EventInterface(EEventGroup.GroupLogic)]` 或 `GroupUI`。
2. 保存代码，点击 Unity 顶部 **Tools/Generate Event Bridge**，检查输出列表后生成。
3. 在该业务程序集的启动入口调用生成的 `GameEventHelper.Init()`。
4. 通过 `GameEvent.Get<TInterface>().Method(...)` 发送，通过生成的 `GameEventHelper.AddEvent/RemoveEvent` 或实例 `GameEventMgr` 管理订阅。

示例见 `Examples/IPlayerEvent.cs`：

```csharp
using AshFramework.Events;
using AshFramework.Events.Examples;

GameEventHelper.Init();
GameEventHelper.AddEvent.PlayerEvent.HpChanged(OnHpChanged);
GameEvent.Get<IPlayerEvent>().HpChanged(80, 100);
GameEventHelper.RemoveEvent.PlayerEvent.HpChanged(OnHpChanged);
```

不同命名空间的 `GameEventHelper` 请通过别名或完整类型名区分。每个事件命名空间分别初始化；没有隐式全工程自动初始化。

## 编写事件接口的限制

推荐按下面的格式编写：接口使用 `public`，方法只声明签名，不写访问修饰符或实现。

```csharp
using AshFramework.Events;

namespace MyGame.Events
{
    [EventInterface(EEventGroup.GroupLogic)]
    public interface IPlayerEvent
    {
        void HpChanged(int currentHp, int maxHp);
        void Died();
    }
}
```

接口签名校验失败会报告文件与接口名称，并且不应用本轮生成结果；扫描范围和命名约定也需按下表遵守。

| 项目 | 当前要求与限制 |
| --- | --- |
| 源文件位置 | 放在工程 `Assets` 下的普通 `.cs` 文件中；扫描会跳过路径中名为 `Editor`、`Generated` 的目录，以及名称以 `.` 开头或以 `~` 结尾的路径段。不要把手写接口放入这些目录。 |
| C# 语法 | 使用 Unity 2022.3 对应的 C# 9 语法范围；命名空间使用上面的花括号形式，不使用 C# 10 的文件范围命名空间 `namespace MyGame.Events;`。 |
| 事件属性 | 使用 `[EventInterface(...)]` 或 `[EventInterfaceAttribute(...)]`，可写命名空间限定形式；给事件属性类型起 `using` 别名后再使用别名标记，扫描器不会识别该接口。 |
| 接口声明 | 必须是 `public`，不支持嵌套在类或其他类型中的接口、泛型接口、`partial` 接口，也不支持继承其他接口。 |
| 接口成员 | 只能包含普通方法；不能包含属性、索引器、C# `event` 或其他成员。 |
| 方法返回值 | 必须是 `void`；不支持返回值、`Task` 或 `UniTask` 返回类型。异步业务处理放在监听者内部。 |
| 方法声明 | 不写 `public`、`private`、`static`、`abstract` 等方法修饰符；不支持泛型方法、方法体或表达式体形式的默认实现。接口本身的 `public` 修饰符仍然必须保留。 |
| 方法名称 | 同一个接口中方法名必须唯一，即使参数不同也不能重载。 |
| 参数 | 每个方法支持 0～6 个普通参数；不支持 `ref`、`out`、`in`、`params` 和指针参数。生成代码中引用的参数类型必须能被接口所属程序集访问。 |
| 参数默认值 | 推荐不依赖默认参数。生成代理的方法声明不会保留参数默认值；如果接口声明默认值，应通过接口类型调用，不能假定代理类型也提供可选参数。 |
| 接口命名 | 推荐 `I` 加大写名称，例如 `IPlayerEvent`；辅助类会去掉这一前缀，得到 `GameEventHelper.AddEvent.PlayerEvent`。避免在同一命名空间同时定义 `IPlayerEvent` 与 `PlayerEvent`，否则辅助类名称会冲突。 |
| 命名空间与程序集 | 每个事件接口的完整类型名必须唯一。同一事件命名空间不能分布在多个程序集内；需要跨程序集定义接口时，使用不同命名空间。 |
| 程序集引用 | 接口所属程序集需要能够引用 `AshFramework.GameEvent`；参数类型的定义程序集也必须可访问。目前不支持通过 `.asmref` 定位接口所属程序集。 |
| 条件编译 | 仅处理本轮 `--defines` 或菜单传入符号下生效的接口和方法；平台条件改变后，需要按目标平台符号重新生成并检查变化列表。 |

以下写法不支持，不能放入实际需要生成的事件接口中：

```csharp
// 不支持：泛型接口或接口继承。
public interface IChanged<T> { }
public interface IChildEvent : IParentEvent { }

// 不支持的事件方法签名示例：
// int QueryHp();                         // 返回值不是 void
// public void Changed(int value);        // 方法包含显式修饰符
// void Changed<T>(T value);              // 泛型方法
// void Changed(ref int value);           // ref 参数
// void Changed(params int[] values);     // params 参数
// void Changed() { }                     // 默认实现
// void Changed(int value);
// void Changed(string value);            // 同名重载
```

`GroupUI` 与 `GroupLogic` 只是分类信息，不改变生成目录，也不会生成两个独立事件中心。

## 手动生成工具

菜单使用独立 .NET 源码解析工具；不是 Source Generator，也不在编译、资源导入、构建或域重载时运行。需要本机已有 .NET 8 SDK，不自动安装。Roslyn 来自 SDK，不把编译分析器 DLL 放进 Assets。

工具源文件采用 `.cs.txt`，Unity 不将其作为脚本编译。生成工具自己的 obj/bin 位于 `Library/EventBridgeTool`。

如果接口改动使旧代理编译失败，可在 Unity 外运行同一个工具修复，不依赖旧程序集：

```powershell
Set-Location D:\UnityProject\AshFramework\Assets\AshFramework\GameEvent\Editor\GeneratorTool
dotnet build EventBridge.CodeGen.csproj --nologo
dotnet ..\..\..\..\..\Library\EventBridgeTool\bin\net8.0\EventBridge.CodeGen.dll --project D:\UnityProject\AshFramework
# 上一条仅预览；检查后应用：
dotnet ..\..\..\..\..\Library\EventBridgeTool\bin\net8.0\EventBridge.CodeGen.dll --project D:\UnityProject\AshFramework --apply
# 存在过期生成源码时，审查列表后另加 --allow-delete。
```

命令行默认定义 `UNITY_EDITOR;UNITY_INCLUDE_TESTS`。涉及平台条件编译时，通过 `--defines "UNITY_EDITOR;UNITY_STANDALONE_WIN;..."` 传入对应平台的完整符号列表；Unity 菜单自动使用当前 Editor 的脚本编译符号。

## 生成工具的输出路径

以下路径均相对于工程根目录 `D:\UnityProject\AshFramework`。事件桥源码的输出目录由接口所属程序集决定，当前没有手动指定输出目录的命令行参数。

| 接口所在情况 | 输出目录 |
| --- | --- |
| 接口自身目录或祖先目录有 `.asmdef` | 从接口文件所在目录向 `Assets` 逐级查找，使用最近的 `.asmdef` 所在目录，输出到 `<该目录>/Generated/EventBridge/`。不是固定输出到接口文件所在目录。 |
| 自身及祖先目录均没有 `.asmdef` | 输出到 `Assets/AshFramework/GameEvent/Generated/EventBridge/`，与接口一起编入 `Assembly-CSharp`。 |
| 查找过程中遇到 `.asmref`，或同一目录有多个 `.asmdef` | 明确报错，不生成；需要先调整接口的程序集归属。 |

例如，接口在 `Assets/GameScripts/HotUpdate/IEvent/IPlayerEvent.cs`，最近的程序集定义是 `Assets/GameScripts/HotUpdate/HotUpdate.asmdef`，输出就是 `Assets/GameScripts/HotUpdate/Generated/EventBridge/`。如果 `IEvent` 目录自己又有 `.asmdef`，则输出改为 `IEvent/Generated/EventBridge/`。

本模块已存在的实际路径是：

| 内容 | 路径 |
| --- | --- |
| 示例接口 | `Assets/AshFramework/GameEvent/Examples/IPlayerEvent.cs` |
| 示例事件桥源码 | `Assets/AshFramework/GameEvent/Examples/Generated/EventBridge/` |
| PlayMode 测试接口 | `Assets/AshFramework/GameEvent/Tests/Runtime/IEventBridgeTest.cs` |
| PlayMode 测试事件桥源码 | `Assets/AshFramework/GameEvent/Tests/Runtime/Generated/EventBridge/` |
| 生成工具源码和项目文件 | `Assets/AshFramework/GameEvent/Editor/GeneratorTool/` |
| 生成工具的编译产物 | `Library/EventBridgeTool/bin/net8.0/EventBridge.CodeGen.dll` 及其运行依赖 |
| 生成工具的中间文件 | `Library/EventBridgeTool/obj/` |

每个事件桥输出目录包含以下文件：

| 文件名规则 | 内容 |
| --- | --- |
| `<接口名>.<接口哈希>.g.cs` | 该接口的 `Xxx_Event` ID、`Xxx_Gen` 发送代理，以及类型明确的添加/移除监听辅助方法。 |
| `EventBridge.Init.<命名空间哈希>.g.cs` | 该输出目录、该命名空间下的 `GameEventHelper.Init()`；同一命名空间的多个接口共用这一初始化文件。 |
| `.eventbridge-manifest.json` | 工具拥有的生成文件清单，用于识别过期产物。 |

文件名哈希使用接口完整名称或命名空间字符串的 SHA-256 前 12 位十六进制字符。当前示例的实际文件名是 `IPlayerEvent.7ad0e206603f.g.cs` 和 `EventBridge.Init.49c9f89fcc74.g.cs`。哈希后缀是文件命名规则，与事件运行时整数 ID 无关。

生成代码保留接口的命名空间，并不会因为输出目录叫 `Generated` 而改变命名空间。不要手动编辑 `.g.cs` 或清单；修改手写接口后重新生成。生成源码和 Unity 产生的 `.meta` 应纳入版本管理，`Library/EventBridgeTool` 中的工具编译产物和中间文件不需要纳入。

## 生成规则

- 根据接口命名空间生成事件 ID、发送代理、类型明确的添加/移除监听方法和初始化方法。
- 有 asmdef 时输出到该 asmdef 所在目录的 `Generated/EventBridge`，保持与接口同一程序集。无 asmdef 时输出到本模块的 `Generated/EventBridge`，编入 Assembly-CSharp。
- ID 字符串包含接口完整名称和方法名，不把运行时整数 ID 用作存档或网络协议编号。
- 所有输入校验完成后才应用文件；内容相同不重写。
- `.eventbridge-manifest.json` 记录工具拥有的文件；只清理清单中且带本工具标记的过期源码。
- 菜单通过 AssetDatabase 删除过期资源并统一刷新。命令行只删除过期源码，由后续 Unity 导入处理资源状态；不手工编写 GUID 或 .meta。
- 生成文件及 Unity 产生的 .meta 应一起纳入版本管理，确保新机器打开即可编译。
- 初始化可重复调用；Shutdown 后可重新初始化。直接手动 new 同一接口代理仍遵循底层注册 API 的重复注册约束。

## 运行时使用边界

- 两个旧分析器移除后，任意整数 ID 的泛型参数不再有 EVENT001/002 诊断；优先使用生成的类型明确的辅助方法。
- 发送同步执行；隐藏不自动解绑，拥有者销毁应调用 Clear。此迁移保留原分发期间延迟增删行为及原异常/重入边界。

## 验证入口

```powershell
# 工具的源码生成、接口变化、清单和覆盖保护自检
dotnet ..\..\..\..\..\Library\EventBridgeTool\bin\net8.0\EventBridge.CodeGen.dll --self-test

# 同工程 Unity 测试，运行前保存并关闭此工程的 Editor
unity test D:\UnityProject\AshFramework --mode EditMode --filter AshFramework.Events.Tests.Editor
unity test D:\UnityProject\AshFramework --mode PlayMode --filter AshFramework.Events.Tests.EventBridgePlayModeTests
```

本模块没有修改项目设置、包依赖或业务启动场景。测试程序集仅用于 Test Runner，不进入普通 Player。
