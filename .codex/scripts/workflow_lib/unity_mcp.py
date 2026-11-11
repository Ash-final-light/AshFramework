"""Run one structured Unity MCP call over the CLI's stdio transport."""

from __future__ import annotations

from contextlib import suppress
import json
import subprocess
from tempfile import TemporaryFile
from threading import Event, Timer

from .core import WorkflowError


def exchange(arguments, *, cwd, env, timeout, name, parameters):
    frames = []
    expired = Event()
    with TemporaryFile() as diagnostics, subprocess.Popen(
        arguments, cwd=cwd, env=env, stdin=subprocess.PIPE,
        stdout=subprocess.PIPE, stderr=diagnostics,
    ) as process:
        def expire():
            expired.set()
            process.kill()

        def send(method, params=None, request_id=None):
            message = {"jsonrpc": "2.0", "method": method}
            if params is not None:
                message["params"] = params
            if request_id is not None:
                message["id"] = request_id
            process.stdin.write(json.dumps(message).encode("utf-8") + b"\n")
            process.stdin.flush()

        def receive(request_id):
            for line in process.stdout:
                frames.append(line)
                try:
                    response = json.loads(line)
                except ValueError:
                    continue
                if isinstance(response, dict) and response.get("id") == request_id:
                    return response
            return {}

        timer = Timer(timeout, expire)
        timer.start()
        response = {}
        try:
            send("initialize", {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "project-workflow", "version": "1.0"},
            }, 1)
            if isinstance(receive(1).get("result"), dict):
                send("notifications/initialized")
                send("tools/call", {"name": name, "arguments": parameters}, 2)
                response = receive(2)
        except BrokenPipeError:
            pass
        finally:
            timer.cancel()
            process.kill()
            process.wait()
            timer.join()
            with suppress(BrokenPipeError):
                process.stdin.close()

        diagnostics.seek(0)
        stdout, stderr = b"".join(frames), diagnostics.read()
        if expired.is_set():
            raise subprocess.TimeoutExpired(arguments, timeout, output=stdout, stderr=stderr)
        return subprocess.CompletedProcess(arguments, 0 if response else 1, stdout, stderr)


def tool_text(stdout):
    for line in stdout.splitlines():
        try:
            response = json.loads(line)
        except ValueError:
            continue
        if not isinstance(response, dict) or response.get("id") != 2:
            continue
        result = response.get("result")
        if not isinstance(result, dict) or result.get("isError"):
            raise WorkflowError("Unity MCP 工具返回错误；详见命令证据。")
        content = result.get("content")
        if isinstance(content, list) and len(content) == 1 and isinstance(content[0], dict):
            item = content[0]
            if item.get("type") == "text" and isinstance(item.get("text"), str):
                return item["text"]
        break
    raise WorkflowError("Unity MCP 未返回可识别的工具结果；详见命令证据。")
