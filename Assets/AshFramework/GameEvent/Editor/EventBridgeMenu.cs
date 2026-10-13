using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AshFramework.Events.Editor
{
    /// <summary>显式菜单生成；不在编译、资源导入、重载或构建时自动执行。</summary>
    public static class EventBridgeMenu
    {
        private const string MenuPath = "Tools/Generate Event Bridge";
        private const string ToolPath = "Assets/AshFramework/GameEvent/Editor/GeneratorTool";

        [Serializable]
        private sealed class Report
        {
            public string project = null;
            public Change[] changes = null;
        }

        [Serializable]
        private sealed class Change
        {
            public string path = null;
            public string operation = null;
        }

        [MenuItem(MenuPath)]
        public static void Generate()
        {
            try
            {
                var project = Directory.GetParent(Application.dataPath).FullName;
                BuildTool(project);
                var report = Preview(project);
                if (report.changes.Length == 0)
                {
                    Debug.Log("[Event Bridge] 生成文件已是最新，没有文件变化。");
                    return;
                }
                var details = string.Join("\n", report.changes.Select(c => c.operation + " " + Relative(project, c.path)));
                Debug.Log("[Event Bridge] 生成预览：\n" + details);
                if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Generate Event Bridge",
                    "将应用以下生成文件变化：\n" + details, "生成", "取消")) return;

                foreach (var change in report.changes.Where(c => c.operation == "delete"))
                {
                    var assetPath = Relative(project, change.path);
                    if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".g.cs", StringComparison.Ordinal))
                        throw new InvalidOperationException("非法生成文件删除路径：" + assetPath);
                    if (!AssetDatabase.DeleteAsset(assetPath))
                        throw new IOException("无法删除过期生成资源：" + assetPath);
                }
                RunTool(project, "--apply --allow-delete");
                AssetDatabase.Refresh();
                Debug.Log("[Event Bridge] 文件生成完成，共 " + report.changes.Length + " 项变化；编译结果请查看 Console。");
            }
            catch (Exception error) { Debug.LogException(error); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanGenerate() => !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>CLI -executeMethod 入口，只验证已有生成文件，不进行自动生成。</summary>
        public static void CheckGenerated()
        {
            try
            {
                var project = Directory.GetParent(Application.dataPath).FullName;
                BuildTool(project);
                RunTool(project, "--check");
                Debug.Log("[Event Bridge] 生成文件一致性检查通过。");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static Report Preview(string project)
        {
            var result = JsonUtility.FromJson<Report>(RunTool(project, ""));
            if (result == null || result.changes == null) throw new InvalidOperationException("生成器未返回有效报告");
            return result;
        }

        private static void BuildTool(string project)
        {
            EditorUtility.DisplayProgressBar("Event Bridge", "编译源码解析工具（需要已安装 .NET 8 SDK）", 0.2f);
            RunProcess(Path.Combine(project, ToolPath), "build EventBridge.CodeGen.csproj --nologo --verbosity quiet");
        }

        private static string RunTool(string project, string options)
        {
            EditorUtility.DisplayProgressBar("Event Bridge", "检查并生成事件桥", 0.6f);
            var tool = Path.Combine(project, "Library/EventBridgeTool/bin/net8.0/EventBridge.CodeGen.dll");
            var defines = string.Join(";", EditorUserBuildSettings.activeScriptCompilationDefines);
            return RunProcess(Path.Combine(project, ToolPath), Quote(tool) + " --project " + Quote(project)
                + " --defines " + Quote(defines) + " " + options);
        }

        private static string RunProcess(string workingDirectory, string arguments)
        {
            var output = new StringBuilder();
            var errors = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo("dotnet", arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) errors.AppendLine(e.Data); };
                try { process.Start(); }
                catch (Exception error) { throw new InvalidOperationException("无法启动 dotnet；请先安装 .NET 8 SDK，工具不会自动安装依赖。", error); }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(120000))
                {
                    process.Kill();
                    throw new TimeoutException("事件桥生成工具执行超时");
                }
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("事件桥工具失败（" + process.ExitCode + "）：\n" + errors + output);
                return output.ToString();
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
        private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    }
}
