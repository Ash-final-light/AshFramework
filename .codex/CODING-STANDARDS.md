# AI 生成与修改 C# 代码的约束

适用于本项目新增和本次修改的 C# 代码。现有第三方源码、生成代码和与任务无关的文件不批量格式化。

## 必须遵守

1. 使用 Allman 风格，块的左右大括号独占一行。
2. 控制语句即使只有一条语句，也添加大括号并展开。
3. try、catch、finally、lock、块式 using、块式方法和构造函数不得压缩成单行。
4. 包含多条语句的 lambda 按块展开，不在一行串联多个操作。
5. 每行只写一条可执行语句；for 头部不受“分号后换行”约束。
6. 缩进使用 4 个空格，不使用 Tab；文件末尾保留换行，删除行尾空格。
7. 不为了减少输出长度而压缩代码，交付前检查本次变更是否满足规则。
8. 不借格式化修改行为、命名、异常处理策略或无关文件。

## 正确示例

```csharp
if (window == null)
{
    window = OpenConsole();
}

try
{
    s_ToolbarEvent.GetRemoveMethod(true).Invoke(
        null,
        new object[] { m_Draw });
}
catch (Exception exception)
{
    Disable(ConsoleCapabilities.Toolbar, exception);
}

lock (m_Gate)
{
    m_Stopping = true;
    Monitor.PulseAll(m_Gate);
}
```

## 禁止示例

```csharp
if (window == null) window = OpenConsole();
if (window == null) { window = OpenConsole(); }
try { RemoveToolbar(); }
catch (Exception exception) { HandleException(exception); }
lock (m_Gate) { m_Stopping = true; Monitor.PulseAll(m_Gate); }
```

## 工具检查

根目录 `.editorconfig` 同时配置 Allman 和“不保留单行块”。只配置大括号换行不足以展开原有单行块。

`verify --task csharp --include <路径>` 会执行独立 Roslyn 语法布局检查、解决方案验证及只读的 whitespace 和 IDE0011 检查。也可以使用 `format-check --include <路径>` 单独检查块布局，不需要业务程序集或 Pipeline。检查器引用 .NET SDK 自带 Roslyn，无第三方 NuGet 包。

实测本机 dotnet format 会保留部分单行 catch，因此不将它作为展开规则的唯一依据。Roslyn 检查器直接验证块的括号行位置、控制语句完整块和同一行的多个语句，不修改源文件；违反规则时由修改者在当前任务范围内修正。

`.editorconfig` 的效果依赖 IDE/格式化器支持。工具检查覆盖空白和所选分析器，不能代替对所有代码生成规则的人工检查。简单表达式成员、自动属性按所在模块约定处理。

## 参考资料

- [Microsoft C# 格式化选项](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/csharp-formatting-options)
- [IDE0011：添加大括号](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0011)
- [OPC Foundation AI 编码指令](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/.github/copilot-instructions.md#code-style)
- [OpenAI AGENTS.md 说明](https://developers.openai.com/codex/guides/agents-md)

这里采用本项目明确的展开规则，不复制参考项目的框架、语言版本或业务约束。
