"""Explicit Unity project binding and standard Pipeline verification contracts."""

from __future__ import annotations

import json
import time

from .core import WorkflowError, blocked, same_path


READ_ONLY = {"editor_status", "list_open_scenes", "get_scene_hierarchy", "get_selection",
             "get_console_logs", "get_build_settings", "list_tests", "test_status", "recompile_status"}
RESERVED = {"project", "project_path", "project-path", "runtime", "runtime_path", "runtime-path",
            "timeout", "format", "json", "confirm", "dry_run", "detach", "result_only"}


def unwrap(text):
    try:
        value = json.loads(text.lstrip("\ufeff")) if isinstance(text, str) else text
    except ValueError as error:
        raise WorkflowError("CLI 未返回 JSON，请查看命令输出。") from error
    for _ in range(8):
        if isinstance(value, str):
            try:
                value = json.loads(value)
                continue
            except ValueError:
                return value
        if not isinstance(value, dict):
            return value
        if value.get("success", value.get("Success")) is False:
            codes = str(value.get("errors", value.get("Error", "")))
            if any(word in codes for word in ("NO_INSTANCES", "UNREACHABLE", "CONNECTION", "AMBIGUOUS")):
                blocked("目标项目没有可用的 Pipeline 连接；详见命令证据。")
            raise WorkflowError("CLI/Pipeline 报告失败；详见命令证据。")
        if "data" in value and ("success" in value or "command" in value):
            value = value["data"]
        elif "result" in value and (value.get("command") in ("command", "cmd") or "executionTimeMs" in value):
            value = value["result"]
        else:
            return value
    raise WorkflowError("不支持的多层 CLI 返回结构。")


def select_instance(data, project, require_ready=True):
    if not isinstance(data, dict) or not isinstance(data.get("instances"), list):
        blocked("无法识别 CLI 实例清单，不猜测目标。")
    matches = []
    for instance in data["instances"]:
        candidate = instance.get("projectPath") or instance.get("project")
        if isinstance(candidate, dict):
            candidate = candidate.get("path")
        if isinstance(candidate, str) and same_path(candidate, project):
            matches.append(instance)
    if len(matches) != 1:
        blocked(f"目标项目必须恰好匹配一个实例，实际匹配 {len(matches)} 个：{project}")
    instance = matches[0]
    state = instance.get("state", instance.get("status"))
    if isinstance(state, dict):
        state = state.get("status")
    if not isinstance(state, str) or not state:
        blocked("实例缺少可识别的 Editor 状态。")
    if require_ready and state != "ready":
        blocked(f"目标 Editor 不是 ready 状态：{state!r}。")
    return instance


def command_names(data):
    if isinstance(data, dict):
        data = data.get("commands", data.get("tools"))
    if not isinstance(data, list):
        blocked("无法识别命令发现结果，不能确认 Pipeline 能力。")
    names = {item.get("name", item.get("command")) for item in data if isinstance(item, dict)}
    return {name for name in names if isinstance(name, str)}


def selected_tests(data, mode, query, filter_type):
    tests = data.get("tests", data.get("Tests")) if isinstance(data, dict) else None
    if not isinstance(tests, list):
        raise WorkflowError("测试发现结果缺少逐用例清单。")
    names = []
    for item in tests:
        name = item.get("fullName", item.get("FullName", ""))
        item_mode = item.get("mode", item.get("Mode", "")).lower()
        expected_mode = "editmode" if mode == "editor" else "playmode"
        explicit = item.get("explicit", item.get("Explicit", False))
        candidate = name if filter_type == "testName" else item.get("assembly", item.get("Assembly", ""))
        categories = item.get("categories", item.get("Categories", [])) or []
        matches = any(query.casefold() == str(category).casefold() for category in categories) if filter_type == "category" else query.casefold() in candidate.casefold()
        if not explicit and item_mode == expected_mode and matches:
            names.append(name)
    if not names or len(names) != len(set(names)):
        blocked("没有匹配的可执行测试，或测试标识不唯一；不接受零测试。")
    return set(names)


def validate_tests(data, expected):
    if not isinstance(data, dict) or data.get("status") != "completed":
        raise WorkflowError("测试尚未完成。")
    summary = data.get("summary", {})
    fields = ("total", "passed", "failed", "skipped", "inconclusive")
    if any(type(summary.get(field)) is not int or summary[field] < 0 for field in fields):
        raise WorkflowError("测试统计结构无效。")
    if summary["passed"] == 0 or summary["total"] == 0:
        raise WorkflowError("零通过测试或全部跳过，不能通过验收。")
    if summary["failed"] or summary["inconclusive"]:
        raise WorkflowError("测试失败或结果不确定。")
    results = data.get("results", [])
    if not isinstance(results, list) or any(not isinstance(item, dict) for item in results):
        raise WorkflowError("逐测试结果格式无效。")
    names = [item.get("fullName", item.get("FullName", item.get("name", item.get("Name")))) for item in results]
    if len(names) != len(set(names)) or set(names) != expected or len(names) != summary["total"]:
        raise WorkflowError("结果与本次发现的测试标识或数量不一致。")
    counts = dict.fromkeys(fields[1:], 0)
    for item in results:
        status = str(item.get("status", item.get("Status", ""))).lower()
        if status not in counts:
            raise WorkflowError("未知的单项测试结果。")
        counts[status] += 1
    if any(summary[field] != counts[field] for field in counts) or sum(counts.values()) != summary["total"]:
        raise WorkflowError("测试汇总与逐项结果不一致。")
    return summary


class Unity:
    def __init__(self, run):
        self.run = run
        self.context = run.context
        self.cli = self.context.tool("unity")
        self.capabilities = None

    def raw(self, arguments, timeout=45):
        result = self.run.execute([self.cli, *arguments, "--format", "json", "--non-interactive", "--no-log-proxy"], timeout)
        data = unwrap(result.stdout)
        if result.returncode:
            raise WorkflowError(f"Unity CLI 退出码 {result.returncode}，请查看命令证据。")
        return data

    def target(self, require_ready=True):
        data = self.raw(["status", "--project-path", str(self.context.project)])
        return select_instance(data, self.context.project, require_ready)

    def discover(self):
        self.target()
        data = self.raw(["command", "--project-path", str(self.context.project), "--detail", "full", "--limit", "10000"])
        self.capabilities = command_names(data)
        return sorted(self.capabilities)

    def call(self, name, parameters=None, polling=False):
        if self.capabilities is None:
            self.discover()
        if name not in self.capabilities:
            blocked(f"目标项目未注册标准命令：{name}")
        self.target(require_ready=not polling)
        parameters = parameters or {}
        if any(not isinstance(key, str) or not key.replace("_", "").isalnum() or key in RESERVED for key in parameters):
            raise WorkflowError("参数包含保留选项或非法名称。")
        if any(isinstance(value, str) and value.lstrip().startswith("-") for value in parameters.values()):
            raise WorkflowError("参数值不能注入 CLI 选项或覆盖项目目标。")
        arguments = ["command", "--project-path", str(self.context.project), "--timeout", "30", name]
        for key, value in parameters.items():
            arguments.extend(["--" + key, value if isinstance(value, str) else json.dumps(value)])
        return self.raw(arguments)

    def query(self, name, parameters=None):
        if name not in READ_ONLY:
            blocked("只开放明确列出的标准只读查询；不执行任意 eval、菜单或写入命令。")
        return self.call(name, parameters)

    def poll(self, name, terminal, timeout):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                value = self.call(name, polling=True)
            except WorkflowError as error:
                if error.status != "blocked":
                    raise
                # 允许域重载暂时断线；每次观察仍重新核对目标，不重发启动命令。
                time.sleep(min(1, max(0, deadline - time.monotonic())))
                continue
            if isinstance(value, dict) and value.get("status") in terminal:
                return value
            time.sleep(min(1, max(0, deadline - time.monotonic())))
        blocked(f"等待 {name} 超时；操作可能继续执行，请检查实际结果，不重发启动命令。")

    def recompile(self, timeout):
        started = self.call("recompile")
        if not isinstance(started, dict) or started.get("status") not in ("compiling", "up_to_date", "triggered"):
            blocked("未确认本次编译启动，不能使用旧结果。")
        result = self.poll("recompile_status", {"completed", "up_to_date"}, timeout)
        if result.get("failed") or result.get("hasErrors") or result.get("errorCount", 0) or result.get("errors"):
            raise WorkflowError("Editor 编译存在错误。")
        self.capabilities = None
        self.target()
        return result

    def tests(self, mode, query, filter_type, timeout):
        if not query.strip():
            blocked("必须指定相关测试筛选器，不能默认运行所有第三方测试。")
        expected = selected_tests(self.call("list_tests", {"mode": mode}), mode, query, filter_type)
        before = self.call("test_status")
        if isinstance(before, dict) and before.get("status") == "running":
            blocked("目标项目已有测试运行，未启动新的测试。")
        started = self.call("run_tests", {"mode": mode, "filter": query, "filter_type": filter_type, "async_tests": True})
        if not isinstance(started, dict) or started.get("result", started.get("Result")) != "running":
            blocked("未确认本次异步测试启动，不能使用保留的旧测试结果。")
        result = self.poll("test_status", {"completed", "failed", "cancelled"}, timeout)
        return {"mode": mode, "filter": query, "summary": validate_tests(result, expected), "results": result["results"]}
