"""Project resolution, bounded processes and sanitized execution reports."""

from __future__ import annotations

import contextlib
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
import uuid


class WorkflowError(Exception):
    def __init__(self, message, status="failed"):
        super().__init__(message)
        self.status = status


def blocked(message):
    raise WorkflowError(message, "blocked")


def timestamp():
    return datetime.now(timezone.utc).isoformat()


def same_path(left, right):
    return os.path.normcase(str(Path(left).resolve())) == os.path.normcase(str(Path(right).resolve()))


SECRET_KEY = re.compile(r"token|password|passwd|secret|authorization|api[-_]?key", re.I)
SECRET_TEXT = re.compile(
    r"(?i)((?:--)?(?:access[-_]?token|api[-_]?key|password|passwd|secret|authorization)"
    r"[\"']?\s*(?:[:=]\s*|\s+)[\"']?)([^\s\"',;}]+)"
)


def redact(value):
    if isinstance(value, dict):
        return {str(key): "[REDACTED]" if SECRET_KEY.search(str(key)) else redact(item)
                for key, item in value.items()}
    if isinstance(value, list):
        result = []
        hide_next = False
        for item in value:
            result.append("[REDACTED]" if hide_next else redact(item))
            hide_next = isinstance(item, str) and bool(re.fullmatch(r"--[\w-]+", item)) and bool(SECRET_KEY.search(item))
        return result
    if isinstance(value, str):
        value = re.sub(r"(?i)\bBearer\s+[^\s\"',;}]+", "Bearer [REDACTED]", value)
        value = re.sub(r"(https?://)[^/@\s]+:[^/@\s]+@", r"\1[REDACTED]@", value)
        value = re.sub(r"\bsk-[A-Za-z0-9_-]{20,}", "[REDACTED]", value)
        return SECRET_TEXT.sub(r"\1[REDACTED]", value)
    return value


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(redact(value), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


class Context:
    def __init__(self, project=None):
        self.project = Path(project or Path(__file__).resolve().parents[3]).resolve()
        for name in ("Assets", "Packages", "ProjectSettings"):
            if not (self.project / name).is_dir():
                blocked(f"不是 Unity 项目，缺少目录：{self.project / name}")
        self.root = self.project / ".codex"
        self.runs = self.root / "runs"
        path = self.root / "workflow.json"
        try:
            self.config = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, ValueError) as error:
            blocked(f"无法读取工作流配置 {path}：{error}")
        if self.config.get("schemaVersion") != 1:
            blocked("不支持的 workflow.json schemaVersion。")

    def tool(self, name):
        configured = self.config.get("unity_cli") if name == "unity" else None
        if configured:
            candidate = Path(configured)
            if not candidate.is_absolute():
                candidate = self.project / candidate
            if not candidate.is_file():
                blocked(f"配置的 CLI 不存在：{candidate}")
            return str(candidate.resolve())
        found = shutil.which(name)
        if not found:
            blocked(f"PATH 中未找到工具：{name}；不会自动安装。")
        return found

    def solution(self, override=None):
        selected = override or self.config.get("solution")
        if selected:
            path = Path(selected)
            path = path if path.is_absolute() else self.project / path
            path = path.resolve()
            if not path.is_relative_to(self.project) or not path.is_file():
                blocked("解决方案必须是当前项目内已有的文件。")
            return path
        candidates = sorted([*self.project.glob("*.sln"), *self.project.glob("*.slnx")])
        preferred = [path for path in candidates if path.stem == self.project.name]
        if len(preferred) == 1:
            return preferred[0]
        if len(candidates) != 1:
            blocked("解决方案不存在或不唯一，请在 Unity 同步项目文件或显式指定 --solution。")
        return candidates[0]


class Run:
    def __init__(self, context, command):
        self.context = context
        name = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8]
        self.path = context.runs / name
        self.path.mkdir(parents=True)
        self.data = {"schemaVersion": 1, "project": str(context.project), "command": command,
                     "startedAt": timestamp(), "finishedAt": None, "status": "running", "steps": [], "commands": []}
        self.save()

    def save(self):
        write_json(self.path / "report.json", self.data)

    def step(self, name, action, required=True):
        entry = {"name": name, "required": required, "status": "running", "startedAt": timestamp()}
        self.data["steps"].append(entry)
        self.save()
        try:
            entry.update(status="passed", result=redact(action()))
        except WorkflowError as error:
            entry.update(status=error.status, message=str(error))
        except Exception as error:
            entry.update(status="failed", message=f"{type(error).__name__}: {error}")
        entry["finishedAt"] = timestamp()
        self.save()
        return entry["status"] == "passed"

    def skip(self, name, reason):
        self.data["steps"].append({"name": name, "required": True, "status": "blocked", "message": reason})
        self.save()

    def execute(self, arguments, timeout=60):
        arguments = [str(item) for item in arguments]
        number = len(self.data["commands"]) + 1
        record = {"args": redact(arguments), "cwd": str(self.context.project), "startedAt": timestamp()}
        self.data["commands"].append(record)
        self.save()
        environment = dict(os.environ)
        environment["PYTHONUTF8"] = "1"
        # Runtime selectors override --project-path in Unity CLI; never inherit them.
        for key in ("UNITY_RUNTIME", "UNITY_RUNTIME_PATH", "UNITY_PROJECT_PATH"):
            environment.pop(key, None)
        try:
            completed = subprocess.run(arguments, cwd=self.context.project, env=environment,
                                       capture_output=True, timeout=timeout, shell=False)
            stdout = completed.stdout.decode("utf-8-sig", errors="replace")
            stderr = completed.stderr.decode("utf-8-sig", errors="replace")
            record["returncode"] = completed.returncode
        except subprocess.TimeoutExpired as error:
            stdout = (error.stdout or b"").decode("utf-8-sig", errors="replace")
            stderr = (error.stderr or b"").decode("utf-8-sig", errors="replace")
            record["returncode"] = None
            record["timedOut"] = True
            completed = None
        except OSError as error:
            blocked(f"无法执行工具：{error}")
        finally:
            record["finishedAt"] = timestamp()
        for kind, text in (("stdout", stdout), ("stderr", stderr)):
            filename = f"command-{number:02d}.{kind}.log"
            (self.path / filename).write_text(redact(text), encoding="utf-8")
            record[kind] = filename
        self.save()
        if completed is None:
            blocked("命令等待超时；结果未知，请先检查实际状态，不自动重发。")
        return subprocess.CompletedProcess(arguments, completed.returncode, stdout, stderr)

    def finish(self):
        required = [step["status"] for step in self.data["steps"] if step["required"]]
        self.data["status"] = "failed" if "failed" in required else (
            "blocked" if any(status != "passed" for status in required) or not required else "passed")
        self.data["finishedAt"] = timestamp()
        self.save()
        render_report(self.path)
        return {"passed": 0, "failed": 1, "blocked": 2}[self.data["status"]]


def render_report(path):
    path = Path(path)
    data = json.loads((path / "report.json").read_text(encoding="utf-8"))
    text = ["# 工作流执行报告", "", f"- 结果：{data['status']}", f"- 项目：{data['project']}",
            f"- 命令：`{data['command']}`", f"- 开始：{data['startedAt']}", f"- 结束：{data['finishedAt']}",
            "", "## 验证步骤", ""]
    for step in data["steps"]:
        requirement = "必需" if step["required"] else "诊断项"
        text.append(f"- **{step['name']}**：{step['status']}（{requirement}）")
        if step.get("message"):
            text.append("  " + step["message"].replace("\n", "\n  "))
        if "result" in step:
            text.extend(["", "```json", json.dumps(step["result"], ensure_ascii=False, indent=2), "```", ""])
    text.extend(["", "## 命令证据", ""])
    for command in data["commands"]:
        text.append("- `" + " ".join(command["args"]).replace("`", "'") + "`")
        for kind in ("stdout", "stderr"):
            if kind in command:
                text.append(f"  - [{kind}]({command[kind]})")
    (path / "report.md").write_text(redact("\n".join(text)) + "\n", encoding="utf-8")
    return data["status"]


def require_success(result, description):
    if result.returncode:
        raise WorkflowError(f"{description}失败，退出码 {result.returncode}；查看本次命令证据。")
    return result.stdout


@contextlib.contextmanager
def project_lock(context):
    context.runs.mkdir(parents=True, exist_ok=True)
    path = context.runs / "editor.lock"
    try:
        descriptor = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    except FileExistsError:
        blocked(f"项目正在执行受锁保护的操作：{path}。请核查持有者，不自动移除锁。")
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump({"pid": os.getpid(), "project": str(context.project), "createdAt": timestamp()}, stream)
        yield
    finally:
        path.unlink(missing_ok=True)
