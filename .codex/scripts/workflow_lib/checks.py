"""Independent workflow checks, environment diagnosis and task-selected validation."""

from __future__ import annotations

import ast
import json
from pathlib import Path
import re
import shutil
import sys
import xml.etree.ElementTree as ET

from .core import WorkflowError, blocked, project_lock, require_success, same_path
from .unity import Unity


PROFILES = {"docs", "code", "unity", "full"}


def select_profile(config, task=None, profile=None):
    if bool(task) == bool(profile):
        raise WorkflowError("请指定 --task 或 --profile 其中一个。")
    selected = profile or config.get("task_profiles", {}).get(task)
    if selected not in PROFILES:
        raise WorkflowError(f"未知的任务或验证范围：{task or profile}")
    return selected


def structure(context):
    required = ["AGENTS.md", ".editorconfig", ".codex/README.md", ".codex/CODING-STANDARDS.md",
                ".codex/scripts/workflow.py", ".codex/tests/test_workflow.py",
                ".codex/tools/CSharpLayoutCheck/Program.cs", ".codex/tools/CSharpLayoutCheck/CSharpLayoutCheck.csproj"]
    missing = [name for name in required if not (context.project / name).is_file()]
    if missing:
        raise WorkflowError("缺少工作流文件：" + ", ".join(missing))
    profiles = context.config.get("task_profiles")
    if not isinstance(profiles, dict) or not profiles or any(value not in PROFILES for value in profiles.values()):
        raise WorkflowError("任务与验证范围映射无效。")
    for path in (context.root / "scripts").rglob("*.py"):
        ast.parse(path.read_text(encoding="utf-8-sig"), filename=str(path))
    content = (context.project / ".editorconfig").read_text(encoding="utf-8-sig")
    for key, value in {"csharp_new_line_before_open_brace": "all", "csharp_preserve_single_line_blocks": "false",
                       "csharp_preserve_single_line_statements": "false", "csharp_prefer_braces": "true:warning"}.items():
        if not re.search(rf"(?m)^\s*{key}\s*=\s*{re.escape(value)}\s*$", content):
            raise WorkflowError(f"缺少约定的 C# 格式配置：{key} = {value}")
    return {"filesChecked": required, "taskProfiles": profiles, "note": "检查本工作流结构与语法，不代表完整 Markdown 链接或业务源码验证。"}


def unit_tests(run):
    result = run.execute([sys.executable, "-m", "unittest", "discover", "-s", run.context.root / "tests", "-v"], 60)
    require_success(result, "工作流单元测试")
    match = re.search(r"Ran (\d+) tests?", result.stderr)
    if not match or int(match[1]) == 0:
        raise WorkflowError("工作流测试没有实际执行用例。")
    return {"tests": int(match[1]), "result": "passed"}


def pipeline_package(context):
    embedded = context.project / "Packages/com.unity.pipeline/package.json"
    cached = sorted((context.project / "Library/PackageCache").glob("com.unity.pipeline@*/package.json"))
    for path in [embedded, *cached]:
        if path.is_file():
            value = json.loads(path.read_text(encoding="utf-8-sig"))
            return {"installed": True, "version": value.get("version"), "path": str(path)}
    manifest = json.loads((context.project / "Packages/manifest.json").read_text(encoding="utf-8-sig"))
    declared = manifest.get("dependencies", {}).get("com.unity.pipeline")
    return {"installed": False, "declared": declared, "note": "未发现本项目已解析的 Pipeline 包；URP 与 Scriptable Build Pipeline 不属于该包。"}


def environment(run):
    context = run.context
    tools = {}
    for name in ("unity", "dotnet", "git"):
        try:
            executable = context.tool(name)
            version = require_success(run.execute([executable, "--version"], 20), name + " 版本检查").strip()
            tools[name] = {"available": True, "path": executable, "version": version}
        except WorkflowError as error:
            tools[name] = {"available": False, "reason": str(error)}
    return {"project": str(context.project), "python": sys.version,
            "unityEditor": (context.project / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8").strip(),
            "tools": tools, "pipelinePackage": pipeline_package(context)}


def inventory(run):
    data = Unity(run).raw(["pipeline", "list"])
    if not isinstance(data, dict) or not isinstance(data.get("instances"), list):
        blocked("无法识别 Pipeline 运行清单。")
    matches = [item for item in data["instances"] if isinstance(item.get("projectPath"), str)
               and same_path(item["projectPath"], run.context.project)]
    if len(matches) > 1:
        blocked("运行清单中目标项目实例不唯一。")
    return {"targetProject": str(run.context.project), "targetInstances": matches,
            "otherProjects": len(data["instances"]) - len(matches),
            "note": "运行清单只是诊断，实际操作仍要求目标项目唯一且 ready。"}


def doctor(run, require_editor=False):
    run.step("environment", lambda: environment(run))
    run.step("pipeline-inventory", lambda: inventory(run), required=False)
    run.step("editor-target", lambda: Unity(run).target(), required=require_editor)


def solution_inputs(context, solution):
    # 此检查读取 Unity 生成的项目清单，不创建或修改项目文件。
    if solution.suffix == ".slnx":
        projects = [node.get("Path") for node in ET.parse(solution).getroot().iter("Project") if node.get("Path")]
    else:
        projects = re.findall(r'Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"', solution.read_text(encoding="utf-8-sig"))
    if not projects:
        blocked("解决方案未包含 C# 项目。")
    sources = set()
    for name in projects:
        path = (solution.parent / name.replace("\\", "/")).resolve()
        if not path.is_relative_to(context.project) or not path.is_file():
            blocked("解决方案引用了不存在或当前项目之外的 csproj，请通过 Unity 同步。")
        for element in ET.parse(path).getroot().iter():
            if element.tag.rsplit("}", 1)[-1] != "Compile" or not element.get("Include"):
                continue
            include = element.get("Include").replace("\\", "/")
            if "*" in include or "$" in include:
                blocked("发现动态 Compile 清单，无法证明源码覆盖；请提供适用的项目适配。")
            sources.add((path.parent / include).resolve())
    return {"projects": len(projects), "sources": sources}


def scoped_paths(context, includes):
    if not includes:
        blocked("请用 --include 指定本次修改的 C# 文件或目录，避免全仓格式化验收。")
    paths = []
    for include in includes:
        path = (context.project / include).resolve()
        if not path.is_relative_to(context.project) or not path.exists():
            raise WorkflowError(f"格式检查范围不存在或超出项目：{include}")
        if any(part in {"Library", "Temp", "Packages", "obj"} for part in path.relative_to(context.project).parts):
            raise WorkflowError("不检查缓存、生成目录和第三方包的格式。")
        paths.append(path)
    sources = sorted({path for item in paths for path in (item.rglob("*.cs") if item.is_dir() else [item]) if path.suffix == ".cs"})
    if not sources:
        blocked("指定范围不包含 C# 源码。")
    return paths, sources


def build(run, solution, includes):
    _, sources = scoped_paths(run.context, includes)
    inputs = solution_inputs(run.context, solution)
    missing = [str(path.relative_to(run.context.project)) for path in sources if path not in inputs["sources"]]
    if missing:
        blocked("所选源码未进入生成解决方案，请通过 Unity 同步：" + ", ".join(missing[:20]))
    result = run.execute([run.context.tool("dotnet"), "build", solution, "--nologo", "--verbosity", "quiet"], 300)
    require_success(result, "解决方案构建")
    return {"solution": str(solution), "projects": inputs["projects"], "scopedSources": len(sources)}


def formatting(run, solution, includes, style=False):
    paths, _ = scoped_paths(run.context, includes)
    command = [run.context.tool("dotnet"), "format", "style" if style else "whitespace", solution,
               "--verify-no-changes", "--no-restore", "--verbosity", "quiet"]
    if style:
        command.extend(["--diagnostics", "IDE0011"])
    command.extend(["--include", *[str(path.relative_to(run.context.project)) for path in paths]])
    require_success(run.execute(command, 180), "C# 代码风格检查" if style else "C# 换行与空白检查")
    return {"scope": includes, "readOnly": True, "check": "IDE0011" if style else "whitespace"}


def layout_check(run, includes):
    _, sources = scoped_paths(run.context, includes)
    project = run.context.root / "tools/CSharpLayoutCheck/CSharpLayoutCheck.csproj"
    output = run.context.runs / "tools/CSharpLayoutCheck"
    dotnet = run.context.tool("dotnet")
    with project_lock(run.context):
        require_success(run.execute([dotnet, "build", project, "--output", output,
                                     "--nologo", "--verbosity", "quiet"], 120), "独立 C# 语法检查器构建")
        require_success(run.execute([dotnet, output / "CSharpLayoutCheck.dll", *sources], 60), "C# 块换行与大括号语法检查")
    return {"files": len(sources), "readOnly": True, "parser": "SDK-bundled Roslyn; no Unity/business references"}


def verify(run, profile, includes, solution_override, test_filter, filter_type, modes, timeout):
    if profile in ("docs", "code", "full"):
        run.step("workflow-structure", lambda: structure(run.context))
        run.step("workflow-tests", lambda: unit_tests(run))
    if profile in ("code", "full"):
        solution = run.context.solution(solution_override)
        run.step("csharp-block-layout", lambda: layout_check(run, includes))
        run.step("solution-build", lambda: build(run, solution, includes))
        run.step("csharp-whitespace", lambda: formatting(run, solution, includes))
        run.step("csharp-braces", lambda: formatting(run, solution, includes, style=True))
    if profile in ("unity", "full"):
        unity = Unity(run)
        with project_lock(run.context):
            connected = run.step("editor-target-and-capabilities", unity.discover)
            compiled = connected and run.step("editor-compilation", lambda: unity.recompile(timeout))
            if not connected:
                run.skip("editor-compilation", "目标校验或命令发现未通过，未启动编译。")
            for mode in modes:
                if not compiled:
                    run.skip(mode + "-tests", "目标连接或 Editor 编译未通过，未启动测试。")
                else:
                    run.step(mode + "-tests", lambda mode=mode: unity.tests(mode, test_filter, filter_type, timeout))
