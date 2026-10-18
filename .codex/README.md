# 通用开发工作流

从 TEngine 工作流中提取项目无关的执行报告、环境诊断、按任务验证、Unity 目标校验。没有迁入业务模块、资源写入、审批系统、Luban 或 HTML 烘焙。Python 脚本仅依赖 Python 3.11+ 标准库；独立 C# 检查器使用本机 .NET SDK 自带 Roslyn，目标运行时跟随 SDK 主版本，无第三方 NuGet 包、Unity 或业务程序集引用。

项目指令入口是根目录 [AGENTS.md](../AGENTS.md)，C# 生成规范在 [CODING-STANDARDS.md](CODING-STANDARDS.md)。AGENTS.md 的加载机制参考 [OpenAI 官方说明](https://developers.openai.com/codex/guides/agents-md)。

## 命令入口

在 Unity 项目根目录运行：

```powershell
python .codex/scripts/workflow.py doctor
python .codex/scripts/workflow.py check
python .codex/scripts/workflow.py verify --task tooling
```

默认项目从脚本自身位置解析，与 shell 当前目录无关。检查其他已经配置此工作流的工程时，在子命令之前传 `--project`：

```powershell
python .codex/scripts/workflow.py --project "D:/path/to/Project" doctor
```

工具从 PATH 查找，不要求 `Tools/unity.exe`。在 [workflow.json](workflow.json) 中可以显式配置 `unity_cli` 路径和 `solution` 文件；相对路径相对于该 Unity 项目。解决方案默认优先选择与项目目录同名的 sln/slnx，否则要求唯一候选，绝不合成项目文件。

## 环境诊断

`doctor` 报告 Python、Unity Editor 配置版本、Unity CLI/.NET/Git 路径及版本、Pipeline 包和运行实例。这里的 Pipeline 是 `com.unity.pipeline`，与 URP、Scriptable Build Pipeline 分开检测。

默认诊断允许没有可连接 Editor：环境采集成功可以是 passed，但报告会单独列出连接 blocked，不能据此声称 Editor 验证通过。需要把连接作为必需项时执行：

```powershell
python .codex/scripts/workflow.py doctor --require-editor
python .codex/scripts/workflow.py unity target
python .codex/scripts/workflow.py unity list
python .codex/scripts/workflow.py unity query list_open_scenes
```

所有 Editor 调用显式绑定规范化绝对项目路径。只接收恰好一个匹配实例，不借其他 ready 项目替代；编译/导入繁忙时不开始新操作。状态轮询可以观察繁忙状态，并持续核对项目。

没有 Pipeline 时不会安装或升级工具、关闭 Editor、换项目，或自动启用另一个验证后端。可以使用已有批处理验证结果作补充证据，但不能把它冒充为 live Pipeline 检查。

命令发现采用 TEngine 参考工作流的无列表选项调用：`unity command --project-path <绝对项目路径> --format json`，不要求 Pipeline 支持 `--detail`、`--limit`。解析器拆开 CLI 外层、命令结果层和 JSON 字符串状态，同时保留测试清单、统计及异步启动确认等业务字段；外层 CLI 成功不能覆盖内层业务失败。

## 按任务选择验证

| 任务 | Profile | 检查范围 |
| --- | --- | --- |
| `docs`、`tooling` | docs | 本工作流结构、Python 语法、格式配置、工作流单元测试 |
| `csharp` | code | docs + 独立 C# 语法布局检查 + 解决方案构建 + 指定范围格式检查 |
| `editor`、`scene`、`prefab` | unity | 目标与能力发现 + Editor 编译 + 明确筛选的测试 |
| `integration` | full | docs + code + unity |

可以使用 `--profile docs/code/unity/full` 显式选择；映射配置在 workflow.json。

```powershell
python .codex/scripts/workflow.py verify --task docs
python .codex/scripts/workflow.py verify --task csharp --include Assets/MyModule/MyClass.cs
python .codex/scripts/workflow.py verify --task editor --test-filter MyModule.Tests
python .codex/scripts/workflow.py verify --task editor --test-filter MyModule.Tests --test-mode playmode
python .codex/scripts/workflow.py verify --profile full --include Assets/MyModule --test-filter MyModule.Tests --test-mode editor --test-mode playmode
```

- `--include` 可以重复。只读格式检查需要指定本次修改范围，防止用全仓历史格式问题扩大任务。
- 格式检查使用 `dotnet format --verify-no-changes --no-restore`；whitespace 检查换行，style 的 IDE0011 检查控制语句大括号，不修改文件。
- 本机格式化器会保留部分单行 catch，因此额外使用 Roslyn 语法树检查块大括号的行位置和控制语句的完整块，不靠正则匹配 C#；字符串、注释中的大括号不会误判。独立命令：`python .codex/scripts/workflow.py format-check --include <C#文件或目录>`，不需要解决方案或 Pipeline。
- 编译前核对所选源码已进入生成解决方案，遗漏时要求通过 Unity 同步项目文件，不伪造构建成功。
- 测试筛选器必须明确，默认按测试名；可选 `--filter-type assembly/category`。
- Editor 验证默认 EditMode；需要 PlayMode 时显式增加 `--test-mode playmode`。
- 测试先发现再运行，检查本次启动确认、终态、数量、逐项结果及标识；零测试、全部跳过、保留旧结果均不通过。
- 测试发现使用无参数 `list_tests`，再在本地按模式、筛选器和 Explicit 标记选择用例。若 CLI 报告 `too old to parse command lines`，说明它不能向当前 Pipeline 传递启动测试所需的参数，该步骤记录为 blocked，不重发、不去掉测试筛选器重试，也不自动升级包。零匹配时在发现阶段直接 blocked，不启动测试。
- 标准 Pipeline 命令协议需要目标包实际提供；不匹配时明确 blocked/failed，不猜测调用参数。
- `docs` 只验证工作流自身结构和语法，不等于完整业务文档、全部链接或 C# 检查。
- scene/prefab 任务的机器测试不代替视觉验收；本工作流不提供资源修改功能。

## 执行报告

每次诊断/验证创建 `.codex/runs/<UTC时间>-<唯一标识>/`，其中包含：

```text
report.json                 结构化结果、选择的任务/Profile、步骤与命令
report.md                   可读报告
command-01.stdout.log        标准输出
command-01.stderr.log        标准错误
```

参数、JSON 结果和日志中的常见 token/password/secret/Bearer 信息会脱敏。报告不记录环境变量或原始进程命令行。通用脱敏无法识别所有自定义业务秘密，调用者仍不应把凭据放入命令参数。

退出码：`0=passed`，`1=failed`，`2=blocked/未完成`。必需项 failed 优先于 blocked，非必需诊断项始终单独列出。超时记录为结果未知，不重发启动命令。

从已有 JSON 重新生成报告，不重跑命令：

```powershell
python .codex/scripts/workflow.py report --run .codex/runs/<运行目录>
```

运行记录、锁和 Python 缓存已加入 Git 忽略。源脚本、配置、测试和规范保留版本管理。项目锁只协调这套工作流，不阻止用户在 Unity 中手动操作。

## 维护与迁移

复制根目录 AGENTS.md、.editorconfig 和 `.codex` 中的源文件即可作为其他 Unity 项目的起点；调整自己的文档入口，不复制 runs 或缓存。没有 Skill 数量、框架目录、业务类型或测试名称硬编码。

新增/修改脚本后运行 `check` 或 `verify --task tooling`。测试使用临时工程和模拟 CLI 响应，独立验证跨项目拒绝、零测试拒绝、报告脱敏、超时、路径和任务路由，不连接真实 Editor。

独立布局检查器的正反样例回归使用 `python .codex/scripts/check_layout_regression.py`：覆盖展开格式、字符串/注释中的括号、lambda/for、压缩 if/try/catch/lock、缺少大括号和同行多语句。样例仅生成到忽略的 runs，不写入 Assets。此项需要 .NET SDK，不属于仅依赖 Python 的 docs 检查。
