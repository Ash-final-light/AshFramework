#!/usr/bin/env python3
"""Exercise the independent C# layout checker against positive/negative fixtures."""

import sys

from workflow_lib.core import Context, Run, WorkflowError, project_lock, require_success


CASES = [
    ("expanded-if-try-catch", True,
     "        if (window == null)\n        {\n            window = new object();\n        }\n"
     "        try\n        {\n            Save();\n        }\n"
     "        catch (System.Exception exception)\n        {\n            Handle(exception);\n        }"),
    ("string-and-comment-braces", True,
     '        var text = "try { Save(); } catch { Ignore(); }";\n'
     "        // if (window == null) { invalid(); }\n        /* catch (Exception e) { ignored(); } */"),
    ("lambda-and-for", True,
     "        System.Action action = () =>\n        {\n            Save();\n        };\n"
     "        for (int index = 0; index < 2; index++)\n        {\n            Save();\n        }"),
    ("compressed-if", False, "        if (window == null) { window = new object(); }"),
    ("unbraced-if", False, "        if (window == null)\n            window = new object();"),
    ("compressed-catch", False,
     "        try\n        {\n            Save();\n        }\n"
     "        catch (System.Exception exception) { Handle(exception); }"),
    ("compressed-try", False,
     "        try { Save(); }\n        catch (System.Exception exception)\n        {\n            Handle(exception);\n        }"),
    ("compressed-lock", False, "        lock (window) { Save(); }"),
    ("multiple-statements", False, "        Save(); Save();"),
]


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    run = Run(Context(), "independent C# layout regression")
    project = run.context.root / "tools/CSharpLayoutCheck/CSharpLayoutCheck.csproj"
    output = run.context.runs / "tools/CSharpLayoutCheck"

    def execute():
        dotnet = run.context.tool("dotnet")
        with project_lock(run.context):
            require_success(run.execute([dotnet, "build", project, "--output", output,
                                         "--nologo", "--verbosity", "quiet"], 120), "检查器构建")
            for name, accepted, body in CASES:
                source = run.path / (name + ".cs")
                source.write_text("class Fixture\n{\n    void Run(object window)\n    {\n" + body + "\n    }\n}\n", encoding="utf-8")

                def check(source=source, accepted=accepted):
                    result = run.execute([dotnet, output / "CSharpLayoutCheck.dll", source], 30)
                    if accepted and result.returncode != 0:
                        raise WorkflowError("合法样例被拒绝，检查命令证据。")
                    if not accepted and (result.returncode != 1 or "LAYOUT001" not in result.stderr):
                        raise WorkflowError("不合规样例未被真实布局诊断拒绝。")
                    return {"expectedAccepted": accepted, "actualExitCode": result.returncode}

                run.step(name, check)
        return {"cases": len(CASES)}

    run.step("layout-regression", execute)
    code = run.finish()
    print(f"{run.data['status']}: {run.path / 'report.md'}")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
