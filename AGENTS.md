# 项目开发约定

## 范围与修改

- 保留用户已有修改，只修改当前任务需要的文件。
- 先核实实际源码、方法签名和程序集边界，不编造 API。
- Unity 场景、Prefab 和资源操作使用适用的 Editor 能力；不把另一项目的结果作为本项目验证。
- 不手改 Unity 生成的 csproj/sln，不为通过检查修改第三方包或批量重排无关代码。

## C# 代码生成格式

- 使用 Allman 风格：块的左、右大括号分别独占一行。
- if/else、for/foreach、while/do 即使只有一条语句也使用大括号并完整展开。
- try/catch/finally、lock 和块式 using 必须完整展开。
- 块式方法、构造函数、局部函数及包含语句块的 lambda 必须展开，禁止单行块。
- 每行只写一条可执行语句，不用分号把多个操作挤到一行；for 头部的分号除外。
- 使用 4 空格缩进，不使用 Tab；移除行尾空格，文件末尾保留换行。
- 不为了缩短回答或减少行数压缩代码。修改已有代码时，对本次修改的代码执行同样规则。
- 简单表达式成员与自动属性按所在模块约定；不能用它们绕过控制流代码块的展开规则。
- 代码注释的说明文字只能使用中文，包括单行注释、多行注释和 XML 文档注释；不使用英文说明或中英双语重复说明。代码标识符、API 名称、XML 标签及数学公式保留原文。
- 规则与正反示例见 [.codex/CODING-STANDARDS.md](.codex/CODING-STANDARDS.md)，格式化配置见 [.editorconfig](.editorconfig)。

## 按任务验证

- 工作流入口：`python .codex/scripts/workflow.py`。用法见 [.codex/README.md](.codex/README.md)。
- 文档任务：`verify --task docs`；工作流脚本任务：`verify --task tooling`。
- C# 任务：`verify --task csharp --include <本次修改的文件或目录>`；解决方案无法自动确定时传 `--solution`。
- 只检查块换行时用 `format-check --include <路径>`，不需要业务程序集或 Pipeline；修改检查器后运行 `python .codex/scripts/check_layout_regression.py`。
- Editor/场景/Prefab 任务：`verify --task editor --test-filter <相关测试名称>`。默认 EditMode，PlayMode 用 `--test-mode playmode`。
- 同时需要全部检查时使用 `verify --profile full`，提供相关测试筛选器及格式检查范围。
- 工作流脚本本身只依赖 Python 3.11+ 标准库；不得自动安装工具、Pipeline 或项目依赖。
- Editor 验证前确认规范化绝对项目路径和唯一 ready 实例，每次调用显式传递目标路径。
- 零测试、全部跳过、失败、结果与发现的用例不一致，均不得宣称测试通过。
- 缺少工具或 Pipeline 时报告 blocked 和未执行项，不关闭用户的 Editor，也不借其他项目补验收。
- 执行记录位于忽略的 `.codex/runs/`，区分 passed、failed、blocked；机器检查不代替视觉验收。
