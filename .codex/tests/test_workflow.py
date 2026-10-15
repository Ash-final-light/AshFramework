"""Regression tests use isolated projects/fake CLI responses, never a real Editor."""

from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

from workflow_lib import checks
from workflow_lib.core import Context, Run, WorkflowError, project_lock, redact, render_report, same_path
from workflow_lib.unity import Unity, command_names, select_instance, selected_tests, unwrap, validate_tests


class ProjectCase(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.project = Path(self.temporary.name) / "RenamedProject"
        for name in ("Assets", "Packages", "ProjectSettings", ".codex"):
            (self.project / name).mkdir(parents=True)
        (self.project / "Packages/manifest.json").write_text('{"dependencies": {}}', encoding="utf-8")
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: test", encoding="utf-8")
        (self.project / ".codex/workflow.json").write_text(json.dumps({"schemaVersion": 1, "task_profiles": {"docs": "docs"}}), encoding="utf-8")
        self.context = Context(self.project)

    def tearDown(self):
        self.temporary.cleanup()

    def test_project_is_resolved_independently_of_working_directory(self):
        self.assertTrue(same_path(self.context.project, self.project))
        with self.assertRaises(WorkflowError):
            Context(self.project.parent)

    def test_solution_uses_current_project_name(self):
        expected = self.project / "RenamedProject.sln"
        expected.touch()
        self.assertEqual(self.context.solution(), expected)

    def test_ambiguous_solution_is_blocked(self):
        (self.project / "One.sln").touch()
        (self.project / "Two.sln").touch()
        with self.assertRaises(WorkflowError):
            self.context.solution()

    def test_solution_cannot_escape_project(self):
        outside = self.project.parent / "outside.sln"
        outside.touch()
        with self.assertRaises(WorkflowError):
            self.context.solution(outside)

    def test_cli_uses_path_instead_of_legacy_tools_directory(self):
        with patch("workflow_lib.core.shutil.which", return_value="global-cli"):
            self.assertEqual(self.context.tool("unity"), "global-cli")

    def test_pipeline_does_not_confuse_render_pipeline(self):
        (self.project / "Packages/manifest.json").write_text(
            '{"dependencies": {"com.unity.render-pipelines.universal": "14.0.12"}}', encoding="utf-8")
        self.assertFalse(checks.pipeline_package(self.context)["installed"])

    def test_pipeline_embedded_package_is_detected(self):
        package = self.project / "Packages/com.unity.pipeline/package.json"
        package.parent.mkdir()
        package.write_text('{"version": "test-version"}', encoding="utf-8")
        self.assertEqual(checks.pipeline_package(self.context)["version"], "test-version")

    def test_reports_aggregate_required_blockers(self):
        run = Run(self.context, "test")
        run.step("optional", lambda: (_ for _ in ()).throw(WorkflowError("offline", "blocked")), required=False)
        run.step("required", lambda: {"ok": True})
        self.assertEqual(run.finish(), 0)
        run.skip("missing-editor", "offline")
        self.assertEqual(run.finish(), 2)
        self.assertEqual(render_report(run.path), "blocked")
        self.assertIn("missing-editor", (run.path / "report.md").read_text(encoding="utf-8"))

    def test_failed_check_is_not_hidden_by_blocked_check(self):
        run = Run(self.context, "test")
        run.step("failure", lambda: (_ for _ in ()).throw(WorkflowError("failed")))
        run.skip("unavailable", "offline")
        self.assertEqual(run.finish(), 1)

    def test_command_output_and_report_are_redacted(self):
        run = Run(self.context, "test --access-token private-value")
        result = run.execute([sys.executable, "-c", "print('access_token=private-value')"])
        self.assertEqual(result.returncode, 0)
        run.step("safe", lambda: {"password": "private-value"})
        run.finish()
        for path in run.path.iterdir():
            self.assertNotIn("private-value", path.read_text(encoding="utf-8"))

    def test_timeout_is_incomplete_not_success(self):
        run = Run(self.context, "test")
        with self.assertRaises(WorkflowError) as error:
            run.execute([sys.executable, "-c", "import time; time.sleep(5)"], timeout=0.05)
        self.assertEqual(error.exception.status, "blocked")
        self.assertTrue(run.data["commands"][0]["timedOut"])

    def test_runtime_environment_cannot_override_editor_target(self):
        run = Run(self.context, "test")
        with patch.dict("os.environ", {"UNITY_RUNTIME_PATH": "other-player", "UNITY_PROJECT_PATH": "other-project"}):
            with patch("workflow_lib.core.subprocess.run", return_value=subprocess.CompletedProcess([], 0, b"ok", b"")) as process:
                run.execute(["fake-cli"])
                environment = process.call_args.kwargs["env"]
                self.assertNotIn("UNITY_RUNTIME_PATH", environment)
                self.assertNotIn("UNITY_PROJECT_PATH", environment)

    def test_project_lock_is_exclusive_and_released(self):
        with project_lock(self.context):
            with self.assertRaises(WorkflowError):
                with project_lock(self.context):
                    self.fail("second owner admitted")
        self.assertFalse((self.context.runs / "editor.lock").exists())

    def test_scoped_format_check_rejects_external_paths(self):
        with self.assertRaises(WorkflowError):
            checks.scoped_paths(self.context, ["../outside.cs"])
        with self.assertRaises(WorkflowError):
            checks.scoped_paths(self.context, [])

    def test_selected_source_must_be_in_solution(self):
        source = self.project / "Assets/Example.cs"
        source.write_text("class Example {}", encoding="utf-8")
        project = self.project / "Example.csproj"
        project.write_text('<Project><ItemGroup><Compile Include="Assets/Example.cs" /></ItemGroup></Project>', encoding="utf-8")
        solution = self.project / "RenamedProject.sln"
        solution.write_text('Project("guid") = "Example", "Example.csproj", "id"', encoding="utf-8")
        self.assertIn(source, checks.solution_inputs(self.context, solution)["sources"])


class ContractTests(unittest.TestCase):
    def test_task_profiles_are_explicit(self):
        config = {"task_profiles": {"documentation": "docs", "feature": "code"}}
        self.assertEqual(checks.select_profile(config, task="documentation"), "docs")
        self.assertEqual(checks.select_profile(config, profile="unity"), "unity")
        with self.assertRaises(WorkflowError):
            checks.select_profile(config, task="unknown")

    def test_nested_cli_envelope_is_unwrapped(self):
        value = {"success": True, "data": {"command": "command", "result": {"status": "completed"}}}
        self.assertEqual(unwrap(json.dumps(value)), {"status": "completed"})

    def test_business_test_acknowledgement_is_preserved(self):
        value = {"success": True, "command": "run_tests", "result": "running"}
        self.assertEqual(unwrap(value), value)

    def test_cli_failure_is_not_inferred_as_success_from_data(self):
        with self.assertRaises(WorkflowError):
            unwrap({"success": False, "data": {"status": "completed"}})

    def test_another_ready_project_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            wanted = Path(folder) / "wanted"
            data = {"instances": [{"project": str(Path(folder) / "other"), "state": "ready"}]}
            with self.assertRaises(WorkflowError):
                select_instance(data, wanted)

    def test_duplicate_target_is_rejected(self):
        wanted = Path.cwd()
        instance = {"project": str(wanted), "state": "ready"}
        with self.assertRaises(WorkflowError):
            select_instance({"instances": [instance, instance]}, wanted)

    def test_multiple_projects_select_only_exact_target(self):
        wanted = Path.cwd()
        correct = {"projectPath": str(wanted), "state": {"status": "ready"}}
        data = {"instances": [{"project": str(wanted.parent), "state": "ready"}, correct]}
        self.assertEqual(select_instance(data, wanted), correct)

    def test_busy_target_requires_explicit_polling(self):
        wanted = Path.cwd()
        data = {"instances": [{"project": str(wanted), "state": "compiling"}]}
        with self.assertRaises(WorkflowError):
            select_instance(data, wanted)
        self.assertEqual(select_instance(data, wanted, require_ready=False)["state"], "compiling")

    def test_unknown_status_schema_is_blocked(self):
        with self.assertRaises(WorkflowError):
            select_instance({"instances": [{"project": str(Path.cwd())}]}, Path.cwd())

    def test_command_discovery_requires_real_names(self):
        self.assertEqual(command_names({"commands": [{"name": "editor_status"}]}), {"editor_status"})
        with self.assertRaises(WorkflowError):
            command_names({"unrecognized": True})

    def test_zero_test_discovery_is_blocked(self):
        with self.assertRaises(WorkflowError):
            selected_tests({"tests": []}, "editor", "Example", "testName")

    def test_discovery_excludes_wrong_mode_explicit_and_partial_categories(self):
        data = {"tests": [
            {"fullName": "one", "mode": "EditMode", "categories": ["UI"]},
            {"fullName": "two", "mode": "PlayMode", "categories": ["UI"]},
            {"fullName": "three", "mode": "EditMode", "categories": ["UIExtra"]},
            {"fullName": "four", "mode": "EditMode", "categories": ["UI"], "explicit": True}]}
        self.assertEqual(selected_tests(data, "editor", "UI", "category"), {"one"})

    def test_summary_is_verified_against_individual_results(self):
        value = self.result()
        self.assertEqual(validate_tests(value, {"one"})["passed"], 1)
        value["results"][0]["status"] = "failed"
        with self.assertRaises(WorkflowError):
            validate_tests(value, {"one"})

    def test_stale_or_zero_results_are_rejected(self):
        for expected in ({"new-test"}, set()):
            with self.assertRaises(WorkflowError):
                validate_tests(self.result(), expected)
        value = self.result()
        value["summary"].update(total=0, passed=0)
        value["results"] = []
        with self.assertRaises(WorkflowError):
            validate_tests(value, set())

    def test_secret_arguments_and_bearer_tokens_are_redacted(self):
        self.assertEqual(redact(["--access-token", "abc"])[1], "[REDACTED]")
        self.assertNotIn("abc", redact('"accessToken": "abc"'))
        self.assertNotIn("abc", redact("Authorization: Bearer abc"))

    def test_every_pipeline_call_uses_explicit_project_selector(self):
        unity = self.fake_unity()
        calls = []
        unity.raw = lambda arguments: calls.append(arguments) or {"ok": True}
        unity.call("list_tests", {"mode": "editor"})
        self.assertIn("--project-path", calls[0])
        self.assertEqual(calls[0][calls[0].index("--project-path") + 1], str(Path.cwd()))

    def test_parameter_values_cannot_inject_runtime_selector(self):
        unity = self.fake_unity()
        for parameters in ({"runtime_path": "other-player"}, {"mode": "--runtime=other-player"}):
            with self.assertRaises(WorkflowError):
                unity.call("list_tests", parameters)

    def test_unknown_queries_do_not_enable_arbitrary_eval(self):
        unity = self.fake_unity()
        with self.assertRaises(WorkflowError):
            unity.query("eval", {"code": "return 1;"})

    @staticmethod
    def fake_unity():
        unity = Unity.__new__(Unity)
        unity.context = SimpleNamespace(project=Path.cwd())
        unity.capabilities = {"list_tests"}
        unity.target = lambda require_ready=True: {"state": "ready"}
        return unity

    @staticmethod
    def result():
        return {"status": "completed", "summary": {"total": 1, "passed": 1, "failed": 0, "skipped": 0, "inconclusive": 0},
                "results": [{"fullName": "one", "status": "passed"}]}


if __name__ == "__main__":
    unittest.main()
