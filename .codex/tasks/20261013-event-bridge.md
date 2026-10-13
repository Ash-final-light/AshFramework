# AshFramework 手动事件桥迁移

- 状态：complete
- 目标：将事件运行时迁移到 AshFramework.Events；通过 Tools/Generate Event Bridge 手动生成普通 C#；在 AshFramework 执行 .NET、Unity EditMode 和 PlayMode 验证。
- 验收：源码不再依赖 TEngine；生成结果可编译；重复生成零变化；源码接口变更不依赖旧程序集；事件分发、退订、重复初始化及 Shutdown 后初始化通过。

## 范围与决策

- 仅修改 Assets/AshFramework/GameEvent，新增验证工具和本记录。
- 保留既有 Core 文件、用户去除的 IMemory 及日志依赖。
- 命名空间：运行时 AshFramework.Events，Editor AshFramework.Events.Editor，测试 AshFramework.Events.Tests。
- 使用已安装 .NET SDK 自带 Roslyn 解析源码。工具源码以 .cs.txt 保存，Unity 不将其作为脚本编译。编译输出放 Library，不增加 UPM 包或 Analyzer DLL。
- 菜单和独立命令行调用同一生成引擎。生成按接口所属程序集输出；先校验全部输入，再应用生成结果。
- 不手工修改 ProjectSettings、Packages、现有场景，不提交或发布。Unity PlayMode 测试自动创建默认 ProjectSettings/SceneTemplateSettings.json，保留该文件；既有 ProjectSettings.asset 修改未恢复或覆盖。

## 授权

- 用户要求在 AshFramework 实施命名空间修改并实测。
- 用户选择仅 Unity Editor PlayMode 实测，不执行 Player 构建。
- 用户确认已保存并关闭 AshFramework Editor，可运行该工程的批量 Unity 测试。
- 用户已明确授权删除 SourceGenerator.dll、GameEventAnalyzer.dll 及两份 .meta，共四个文件，并继续验证。

## 验证证据

- 工程 Unity：2022.3.62f1c1。
- Unity CLI：1.0.0-beta.12；目标无 Pipeline，使用 CLI batch test/run，不连接其他工程。
- .NET SDK：8.0.421、10.0.401 已安装。
- 源码解析工具：`dotnet build EventBridge.CodeGen.csproj`，.NET 8.0.421，0 警告、0 错误。
- 工具 `--self-test`：8/8 通过，覆盖生成源码编译及 using 别名、重复生成、接口增改、重载拒绝、过期清理和用户文件保护。
- 对本工程执行 `--apply` 后 `--check`，生成示例与 PlayMode 测试代理，检查结果 changes=[]。
- 完整 `dotnet build D:\UnityProject\AshFramework\AshFramework.sln`：最终 0 警告、0 错误。报告 Logs/EventBridge-DotNet.log。
- Unity CLI EditMode：过滤 AshFramework.Events.Tests.Editor，5/5 Passed、0 Failed、0 Skipped，CLI exit=0。包含实际调用 Tools/Generate Event Bridge 和一致性检查。报告 Logs/EventBridge-EditMode-Final.xml、Logs/EventBridge-EditMode-Final.log。
- Unity CLI PlayMode：过滤 AshFramework.Events.Tests.EventBridgePlayModeTests，7/7 Passed、0 Failed、0 Skipped，CLI exit=0。覆盖类型参数分发、六参数、退订、重复初始化、Shutdown 后初始化、分发中添加、隐藏/销毁生命周期。报告 Logs/EventBridge-PlayMode.xml、Logs/EventBridge-PlayMode.log。
- 测试命令均显式指定 `--editor-path D:\unityEditor\2022.3.62f1c1\Editor\Unity.exe`，实际工程是 AshFramework。
- 首次 EditMode 携带 -nographics：XML 4/4 Passed，但 Editor 退出超时，CLI TEST_TIMED_OUT；该次不作为通过证据。随后不传 -nographics 的最终 EditMode 与 PlayMode 均正常退出，验收采用最终结果。
- 目标 GameEvent 下 DLL 数量为 0；Unity 已生成新资源 .meta。TEngine 工作树状态与实施前一致。
- 仓库级 `git diff --check` 检出当前 ProjectSettings/ProjectSettings.asset 序列化字段尾部空白；该文件在任务开始前已处于修改状态，本次未修复无关项目设置，不将仓库级空白检查记为通过。

## 剩余风险

- 旧分析器移除后，任意整数 ID 的监听签名不再有 EVENT001/002；推荐类型明确的生成辅助方法。
- 菜单依赖 .NET 8 SDK；未安装时明确报错，不自动安装。工具 global.json 将 SDK 限定到 8.0 的已安装功能版本。
- 按用户选择只进行了 Editor PlayMode 实测，未执行 Player 构建或手机设备测试。
- 用户需要在自己的业务启动入口显式调用相应命名空间的 GameEventHelper.Init；当前新增 Examples 为独立示例，不修改业务场景或启动逻辑。
