// shell 组下沉 handler — 代码执行三件套 + repl execute + terminal_capture 元数据。
// 只用 BCL：写临时 csproj + dotnet build/run 执行代码（无 Roslyn、无引擎 DI）；
// repl execute shell out 到 dotnet-script/pwsh/python3；terminal_capture 因 exe stdout 被重定向，
// 无法抓宿主控制台句柄，降级为元数据输出（如实记录）。




namespace JoinCode.CliNode.Shell;

internal static partial class ShellCommands {
    // ==================== 代码执行（dotnet build/run，非 Roslyn） ====================

    private static async Task<int> ExecuteCsharpCodeAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var code = Required(o, "code", "execute_csharp_code");
        var timeout = Math.Clamp(GetInt(o, "timeout_ms", 30000), 1, 300000);
        try {
            var result = await RunCSharpAsync(code, timeout, ct).ConfigureAwait(false);
            Console.Out.Write(result);
            return 0;
        } catch (TimeoutException) {
            Console.Error.WriteLine($"Code execution timed out (exceeded {timeout}ms)");
            return 2;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Code execution failed: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> EvaluateExpressionAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var expr = Required(o, "expression", "evaluate_expression");
        var code = $"using System;\nclass P {{ static void Main() {{ var result = {expr}; Console.WriteLine(result); }} }}";
        try {
            var result = await RunCSharpAsync(code, 10000, ct).ConfigureAwait(false);
            Console.Out.Write(result);
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine($"evaluate_expression failed: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> TestCodeSnippetAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var code = Required(o, "code", "test_code_snippet");
        var input = Required(o, "test_input", "test_code_snippet");
        var expected = Get(o, "expected_output", "");
        var full = BuildTestCode(code, input);
        try {
            var result = (await RunCSharpAsync(full, 30000, ct).ConfigureAwait(false)).Trim();
            var sb = new StringBuilder();
            sb.AppendLine("## Test Results");
            sb.AppendLine();
            sb.AppendLine("### Output");
            sb.AppendLine("```");
            sb.AppendLine(result);
            sb.AppendLine("```");
            if (expected.Length > 0) {
                var match = result == expected.Trim();
                sb.AppendLine();
                sb.AppendLine($"### Expected output match: {(match ? "✓ Pass" : "✗ Fail")}");
                if (!match) { sb.AppendLine("```"); sb.AppendLine(expected); sb.AppendLine("```"); }
            }
            Console.Out.Write(sb.ToString());
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine($"test_code_snippet failed: {ex.Message}");
            return 2;
        }
    }

    private static string BuildTestCode(string code, string input) {
        // 用普通字符串拼接（raw string + $ 会把代码里的 { } 当内插转义）。
        return "using System;\nclass P {\n  static void Main() {\n    try {\n"
            + code + "\n    } catch (Exception ex) { Console.WriteLine(\"Error: \" + ex.Message); }\n  }\n}";
    }

    private static async Task<string> RunCSharpAsync(string code, int timeoutMs, CancellationToken ct) {
        var tempDir = Path.Combine(Path.GetTempPath(), "csharp_sandbox_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Program.cs"), code, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Sandbox.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """, ct).ConfigureAwait(false);

            var build = await RunProcessCaptureAsync("dotnet", ["build", "--configuration", "Release", "--nologo"], tempDir, 30000, ct).ConfigureAwait(false);
            if (build.exit != 0) throw new InvalidOperationException("Compilation failed: " + (build.stderr.Trim().Length > 0 ? build.stderr : build.stdout));

            var dll = Path.Combine(tempDir, "bin", "Release", "net10.0", "Sandbox.dll");
            var run = await RunProcessCaptureAsync("dotnet", [dll], tempDir, timeoutMs, ct).ConfigureAwait(false);
            var sb = new StringBuilder();
            if (run.stdout.Trim().Length > 0) sb.AppendLine(run.stdout.TrimEnd());
            if (run.stderr.Trim().Length > 0) sb.AppendLine("[stderr] " + run.stderr.TrimEnd());
            if (run.exit != 0) sb.AppendLine($"Exit code: {run.exit}");
            return sb.ToString().Trim();
        } finally {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private static async Task<(int exit, string stdout, string stderr)> RunProcessCaptureAsync(
        string exe, string[] args, string workDir, int timeoutMs, CancellationToken ct) {
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = workDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"无法启动 {exe}");
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { if (!p.HasExited) p.Kill(entireProcessTree: true); throw new TimeoutException(); }
        return (p.ExitCode, await so.ConfigureAwait(false), await se.ConfigureAwait(false));
    }

    // ==================== repl execute / terminal_capture 元数据 ====================

    private static async Task<int> ReplAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var action = Get(o, "action", "status").ToLowerInvariant();
        if (action != "execute") {
            // enable/disable/status 是宿主进程内的工具可见性开关（_replModeEnabled），
            // 无法跨进程改变宿主提示词/工具清单——如实说明。
            Console.Out.WriteLine($"repl {action}: 模式开关在宿主进程内生效，独立 exe 无法改变宿主工具可见性。请使用 jcc mcp_call repl {action}");
            return 0;
        }
        var code = Required(o, "code", "repl");
        var language = Get(o, "language", "csharp").ToLowerInvariant();
        var timeoutMs = Math.Clamp(GetInt(o, "timeout_seconds", 30), 1, 3600) * 1000;
        var tempDir = Path.Combine(Path.GetTempPath(), "jcc_repl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try {
            var (exe, args, file) = language switch {
                "python" or "python3" => ("python3", Array.Empty<string>(), "script.py"),
                "powershell" or "ps1" => ("powershell.exe", new[] { "-NoProfile", "-File" }, "script.ps1"),
                _ => ("dotnet", Array.Empty<string>(), "script.csx")
            };
            var scriptPath = Path.Combine(tempDir, file);
            await File.WriteAllTextAsync(scriptPath, code, ct).ConfigureAwait(false);
            var argList = new List<string>(args) { scriptPath };
            var (exit, stdout, stderr) = await RunProcessCaptureAsync(exe, argList.ToArray(), tempDir, timeoutMs, ct).ConfigureAwait(false);
            if (stdout.Trim().Length > 0) Console.Out.Write(stdout);
            if (stderr.Trim().Length > 0) Console.Error.Write(stderr);
            return exit;
        } finally {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static Task<int> TerminalCaptureAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var captureType = Get(o, "capture_type", "screen");
        var maxLines = Math.Clamp(GetInt(o, "max_lines", 50), 1, 1000);
        // exe 的 stdout 被 CliNodeRunner 重定向到管道，GetStdHandle 拿到的是管道不是用户 ConHost，
        // ReadConsoleOutput 抓不到真实屏幕——降级为元数据（如实标注）。
        var sb = new StringBuilder();
        sb.AppendLine($"Terminal capture ({captureType})");
        sb.AppendLine($"  捕获时间: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"  TERM: {Environment.GetEnvironmentVariable("TERM") ?? "-"}");
        sb.AppendLine($"  SHELL: {Environment.GetEnvironmentVariable("SHELL") ?? "-"}");
        sb.AppendLine($"  max_lines: {maxLines}");
        sb.AppendLine("  注意: 独立 exe 的 stdout 被重定向，无法捕获宿主控制台缓冲区内容；真实屏幕捕获请用 jcc mcp_call terminal_capture");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }
}
