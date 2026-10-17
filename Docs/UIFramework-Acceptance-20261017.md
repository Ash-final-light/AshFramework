# UIModule Unity 内验收记录（2026-10-17）

## 结论与范围

本轮约定的原生 Unity Editor 验收通过：当前全部 22 个 EditMode 用例通过，两处右键入口、首次生成、连续生成、生成配置跨重编译保留，以及 RootUI 创建与 Undo 均已实际检查。

目标始终为 D:/UnityProject/AshFramework，Unity 2022.3.62f1c1；没有使用其他项目补验。用户明确授权通过电脑操作执行原生 Test Runner、菜单、生成器与 Undo。本项目没有 Pipeline 连接，原生界面结果与 Pipeline 工作流结果分开记录。

## 最终验收结果

| 检查 | 结果与证据 |
| --- | --- |
| 原生 Test Runner，EditMode | 发现并选中 UIModule Editor 测试程序集的 22 个用例，全部通过，0 失败、0 未运行，耗时 0.787 秒。逐项查看列表前后段，见 editmode-results-final-22.png、editmode-results-final-tail.png。 |
| Hierarchy 右键菜单 | 实际通过 AshFramework/UIModule/RootUI 创建完整层级。见 hierarchy-menu.png、rootui-before-undo.png。 |
| 创建内容与引用 | MainCanvas、PopupCanvas、OverlayCanvas、未激活的 StagingRoot，以及场景缺少的 EventSystem 均创建；RootUI 的四个 Inspector 引用自动填入。 |
| Inspector 组件右键 | 实际进入 AshFramework/UIModule/生成 Window 基类并打开生成器。见 inspector-menu.png。本轮另通过 Tools 菜单进入同一生成器完成连续生成。 |
| 首次生成 | UIModuleAcceptance 样例写入 4 个 C# 文件，导入并编译；没有自动创建 Prefab。见 generator-first-write.png。 |
| 二次生成与业务文件保护 | 先在 Window、Args、View 三个业务文件中加入有效注释，再将层级 Main 改为 Popup 并点击生成；界面显示写入 1 个文件、保留 3 个已有业务文件。三个业务文件 SHA256 与加入注释后的版本完全一致；生成基类内容和哈希改变，包含 UILayer.Popup。见 regeneration-check.json。 |
| 重编译后配置保留 | 二次生成实际触发脚本编译与程序集重载，完成后窗口名、命名空间、脚本目录、Prefab 路径和 Popup 层级全部保留；写入/保留数量提示也保留。见 generator-regeneration-after-reload.png。 |
| Undo | Edit 菜单显示 Undo Create AshFramework RootUI，执行一次后 RootUI、全部子节点及本次创建的 EventSystem 一并撤销，原 Main Camera 保留；场景恢复无修改星号。见 rootui-before-undo.png、rootui-after-undo.png。 |
| C# 工作流 | 17 个框架/测试源码通过块布局、实际 Unity 生成的解决方案构建、范围格式与大括号检查。清理样例并通过 Unity 重新生成项目文件后，最终报告为 .codex/runs/20261017T075510Z-0cf45e03/report.md。 |

证据目录为 .codex/runs/ui-module-native-acceptance-20261017/。没有导出 NUnit XML；测试结果来自原生 Test Runner 的数量、终态与逐项可见结果，不宣称 Pipeline 测试通过。

## 发现并修复的问题

1. **测试 UIView 所在程序集不正确。** 首次 21 用例运行中 11 通过、10 失败，因为 TestUIView 位于仅 Editor 的程序集，Unity 拒绝将其挂载到 GameObject。已将脚本与 meta 一起移至独立的 Tests/Support 测试程序集，由 Editor 测试程序集显式引用；修复后的 21 用例运行通过。
2. **生成配置在域重载后恢复默认值。** 首次实际生成后发现此问题。已对 UIWindowGenerationOptions 添加 Serializable，并将 EditorWindow 的选项与状态标记为 SerializeField。自动回归与实际连续生成均已验证配置保留。
3. **新增序列化回归测试输入格式不正确。** 本轮首次 22 用例运行中 21 通过、1 失败。原测试手工 JSON 不符合 EditorJsonUtility 对 EditorWindow 的序列化格式。已改为通过 SerializedObject 写入真实选项，使用 EditorJsonUtility 往返后核对五项配置；当前全部 22 用例通过。没有删除或跳过该用例。

此前电脑操作工具多次以 user input was detected in this window; call get_window_state before continuing 拒绝点击，当时记录为 blocked。本轮识别到重新打开的 AshFramework 窗口后，点击恢复，已完成全部上述待验收项。Editor 重新打开发生在本轮接管之前，本轮没有关闭或重启 Editor。

## 清理与项目状态

- 临时场景对象通过原生 Undo 清理；未保存验收场景，原 Main Camera 保留。
- 样例源代码与 Unity 生成的 meta 已归档至证据目录中的 generated-sample-imported/。归档前验证该代码目录在首次生成前不存在、只包含本次样例及对应 meta，并核对最终源码哈希；归档操作没有包含其他文件。见 cleanup.json。
- 已通过原生 Assets/Refresh 让 Unity 识别样例移出 Assets。项目不再保留临时的 Assets/GameScripts 验收目录。
- 没有安装包、修改第三方代码或手改 Unity 生成的 csproj/sln；用户原有 ProjectSettings 修改保留。

清理后首次构建遇到生成的 IDE 工程仍引用已移出的样例及 NETSDK1004 缓存错误；已通过 Unity Preferences / External Tools / Regenerate project files 重新同步，未更改首选项，最终 C# 工作流全部通过。

## 验收边界

22 个 EditMode 用例覆盖生命周期、共享加载、取消、层内顺序、迟到实例清理、关闭重开、回调重入、释放重试、生成文件保护、路径校验和生成配置序列化。服务测试使用受控加载器；本轮不包含真实业务 Prefab 的 Resources 加载、PlayMode 展示、按钮输入或不同分辨率的业务界面验收。

Pipeline Editor 工作流仍因本项目缺少连接而 blocked，最新该项记录为 .codex/runs/20261016T165453Z-83b5b440/report.md。此限制不影响本次已实际完成的、用户授权的原生界面验收结论。
