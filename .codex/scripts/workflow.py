#!/usr/bin/env python3
"""Portable, project-bound diagnostics and evidence; no automatic tool installation."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

from workflow_lib import checks
from workflow_lib.core import Context, Run, WorkflowError, render_report
from workflow_lib.unity import Unity


def parser():
    root = argparse.ArgumentParser(description=__doc__)
    root.add_argument("--project", type=Path, help="明确的 Unity 项目目录；默认使用此脚本所在项目。")
    commands = root.add_subparsers(dest="command", required=True)
    doctor = commands.add_parser("doctor", help="诊断环境；默认将 Editor 不可用记录为非必需诊断项。")
    doctor.add_argument("--require-editor", action="store_true")
    commands.add_parser("check", help="独立检查工作流结构和单元测试。")
    verify = commands.add_parser("verify", help="按任务或显式验证范围执行。")
    choice = verify.add_mutually_exclusive_group(required=True)
    choice.add_argument("--task", help="docs/tooling/csharp/editor/scene/prefab/integration；映射在 workflow.json。")
    choice.add_argument("--profile", choices=sorted(checks.PROFILES))
    verify.add_argument("--include", action="append", default=[], help="本次修改的 C# 文件或目录；可重复。")
    verify.add_argument("--solution", type=Path)
    verify.add_argument("--test-filter", default="")
    verify.add_argument("--filter-type", choices=("testName", "assembly", "category"), default="testName")
    verify.add_argument("--test-mode", action="append", choices=("editor", "playmode"))
    verify.add_argument("--timeout", type=int, default=300)
    unity = commands.add_parser("unity", help="显式目标校验及标准只读查询，不包含任意代码或资源写入。")
    actions = unity.add_subparsers(dest="action", required=True)
    actions.add_parser("target")
    actions.add_parser("list")
    query = actions.add_parser("query")
    query.add_argument("name")
    query.add_argument("--params-file", type=Path)
    layout = commands.add_parser("format-check", help="独立检查指定 C# 源码的块换行，不引用业务程序集。")
    layout.add_argument("--include", action="append", required=True)
    report = commands.add_parser("report", help="从已有 report.json 重新生成 Markdown，不重新执行命令。")
    report.add_argument("--run", required=True, type=Path)
    return root


def dispatch(run, arguments):
    if arguments.command == "doctor":
        checks.doctor(run, arguments.require_editor)
    elif arguments.command == "check":
        run.step("workflow-structure", lambda: checks.structure(run.context))
        run.step("workflow-tests", lambda: checks.unit_tests(run))
    elif arguments.command == "verify":
        if arguments.timeout < 1:
            raise WorkflowError("timeout 必须为正整数。")
        profile = checks.select_profile(run.context.config, arguments.task, arguments.profile)
        run.data["selection"] = {"task": arguments.task, "profile": profile}
        run.save()
        checks.verify(run, profile, arguments.include, arguments.solution, arguments.test_filter,
                      arguments.filter_type, arguments.test_mode or ["editor"], arguments.timeout)
    elif arguments.command == "format-check":
        run.step("csharp-block-layout", lambda: checks.layout_check(run, arguments.include))
    elif arguments.command == "unity":
        unity = Unity(run)
        if arguments.action == "target":
            run.step("editor-target", unity.target)
        elif arguments.action == "list":
            run.step("editor-capabilities", unity.discover)
        else:
            parameters = {}
            if arguments.params_file:
                import json
                parameters = json.loads(arguments.params_file.read_text(encoding="utf-8-sig"))
                if not isinstance(parameters, dict):
                    raise WorkflowError("查询参数必须是 JSON 对象。")
            run.step("unity-query", lambda: unity.query(arguments.name, parameters))


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    arguments = parser().parse_args(argv)
    if sys.version_info < (3, 11):
        print("需要 Python 3.11 或更新版本。", file=sys.stderr)
        return 2
    if arguments.command == "report":
        try:
            status = render_report(arguments.run)
            print(f"{status}: {arguments.run / 'report.md'}")
            return {"passed": 0, "failed": 1, "blocked": 2}.get(status, 2)
        except (OSError, ValueError, KeyError) as error:
            print(str(error), file=sys.stderr)
            return 2
    try:
        context = Context(arguments.project)
    except WorkflowError as error:
        print(str(error), file=sys.stderr)
        return 2
    command = " ".join(argv if argv is not None else sys.argv[1:])
    run = Run(context, command)
    try:
        dispatch(run, arguments)
    except WorkflowError as error:
        run.step("dispatch", lambda: (_ for _ in ()).throw(error))
    except KeyboardInterrupt:
        run.skip("interrupted", "用户中断，检查在途操作，不自动重发。")
    except Exception as error:
        run.step("dispatch", lambda: (_ for _ in ()).throw(error))
    code = run.finish()
    print(f"{run.data['status']}: {run.path / 'report.md'}")
    for step in run.data["steps"]:
        print(f"  {step['name']}: {step['status']}" + (" (诊断项)" if not step["required"] else ""))
        if step.get("message"):
            print("    " + step["message"])
    return code


if __name__ == "__main__":
    raise SystemExit(main())
