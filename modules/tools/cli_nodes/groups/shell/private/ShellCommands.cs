



namespace JoinCode.CliNode.Shell;

/// <summary>
/// shell 分组的本地命令表 — 只用 BCL 启动子进程，不引用引擎 / DI / 沙箱子系统。
/// 27 个工具中 10 个零依赖落地（6 命令 + 代码执行三件套 + repl.execute + terminal_capture 元数据）；
/// 构建队列、后台任务注册表、sandbox_*、workflow_* 绑定宿主进程内单例/安全语义，转发宿主。
/// 参数名与 help.txt 的 [McpToolParameter] 逐一对应，说明不在此重复。
///
/// 安全边界：本独立 exe 不经宿主的 Shell 中间件链与 DangerousCommandCatalog，
/// 调用方需自行负责命令内容；生产场景建议仍通过宿主执行以保留完整守卫。
/// </summary>
internal static partial class ShellCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["bash"] = BashAsync,
            ["powershell"] = PowerShellAsync,
            ["powershell_script"] = PowerShellScriptAsync,
            ["powershell_version"] = PowerShellVersionAsync,
            ["powershell_execution_policy"] = PowerShellExecutionPolicyAsync,
            ["powershell_set_execution_policy"] = PowerShellSetExecutionPolicyAsync,
            // 代码执行三件套（dotnet build/run，非 Roslyn）+ repl.execute + terminal_capture 元数据
            ["execute_csharp_code"] = ExecuteCsharpCodeAsync,
            ["evaluate_expression"] = EvaluateExpressionAsync,
            ["test_code_snippet"] = TestCodeSnippetAsync,
            ["repl"] = ReplAsync,
            ["terminal_capture"] = TerminalCaptureAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    private static Task<int> BashAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var command = RequiredCommand(o, "bash");
        return RunProcessAsync("cmd.exe", ["/c", command], WorkingDirectory(o), TimeoutMs(o), ct);
    }

    private static Task<int> PowerShellAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var command = RequiredCommand(o, "powershell");
        return RunProcessAsync("powershell.exe", ["-NoProfile", "-Command", command], WorkingDirectory(o), TimeoutMs(o), ct);
    }

    private static Task<int> PowerShellScriptAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "script_path", "powershell_script");
        if (!global::System.IO.File.Exists(path))
            throw new ArgumentException($"powershell_script 脚本文件不存在: {path}。正确写法: --script_path 传入存在的 .ps1 路径。");
        var args = new List<string> { "-NoProfile" };
        if (IsTrue(o, "no_profile")) args.Add("-NoProfile");
        var policy = Get(o, "execution_policy", "");
        if (policy.Length > 0) args.AddRange(["-ExecutionPolicy", policy]);
        args.AddRange(["-File", path]);
        var extra = Get(o, "arguments", "");
        if (extra.Length > 0) args.Add(extra);
        return RunProcessAsync("powershell.exe", args.ToArray(), WorkingDirectory(o), TimeoutMs(o), ct);
    }

    private static Task<int> PowerShellVersionAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) =>
        RunProcessAsync("powershell.exe",
            ["-NoProfile", "-Command", "$PSVersionTable | Out-String"],
            WorkingDirectory(o), TimeoutMs(o), ct);

    private static Task<int> PowerShellExecutionPolicyAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var scope = Get(o, "scope", "Process");
        return RunProcessAsync("powershell.exe",
            ["-NoProfile", "-Command", $"Get-ExecutionPolicy -Scope {scope}"],
            WorkingDirectory(o), TimeoutMs(o), ct);
    }

    private static Task<int> PowerShellSetExecutionPolicyAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var policy = Required(o, "policy", "powershell_set_execution_policy");
        var scope = Get(o, "scope", "Process");
        var script = $"Set-ExecutionPolicy -ExecutionPolicy {policy} -Scope {scope}";
        if (IsTrue(o, "force")) script += " -Force";
        return RunProcessAsync("powershell.exe",
            ["-NoProfile", "-Command", script],
            WorkingDirectory(o), TimeoutMs(o), ct);
    }

    private static async Task<int> RunProcessAsync(
        string exe, string[] args, string workDir, int timeoutMs, CancellationToken ct) {
        var psi = new ProcessStartInfo(exe) {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动 {exe}，请确认已安装并在 PATH 中。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new ArgumentException(
                $"命令执行超时 {timeoutMs}ms（ShellCommands 超时保护层拦截，子进程已终止）。正确写法: 增大 --timeout，或把长命令拆成更小步骤。");
        }
        Console.Out.Write(await stdout.ConfigureAwait(false));
        Console.Error.Write(await stderr.ConfigureAwait(false));
        return process.ExitCode;
    }

    private static string RequiredCommand(IReadOnlyDictionary<string, string> o, string toolId) {
        var command = Required(o, "command", toolId);
        if (command.Contains('\0'))
            throw new ArgumentException($"{toolId} 的 --command 含空字符（ShellCommands 输入校验层拦截）。正确写法: 传入不含控制字符的命令。");
        return command;
    }

    private static string WorkingDirectory(IReadOnlyDictionary<string, string> o) {
        var dir = Get(o, "working_directory", Directory.GetCurrentDirectory());
        if (!Directory.Exists(dir))
            throw new ArgumentException($"工作目录不存在: {dir}（ShellCommands 输入校验层拦截）。正确写法: --working_directory 指向存在的目录。");
        return dir;
    }

    private static int TimeoutMs(IReadOnlyDictionary<string, string> o) {
        var value = GetInt(o, "timeout", 180000);
        return Math.Clamp(value, 1000, 600000);
    }

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string toolId) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{toolId} 缺少必填参数 --{key}（由 [McpToolParameter] 标记为必填）。正确写法: jcc-tool-shell.exe {toolId} --{key} <值>");

    private static string Get(IReadOnlyDictionary<string, string> o, string key, string fallback) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool IsTrue(IReadOnlyDictionary<string, string> o, string key) =>
        o.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;

    private static int GetInt(IReadOnlyDictionary<string, string> o, string key, int fallback) =>
        int.TryParse(Get(o, key, string.Empty), out var parsed) ? parsed : fallback;
}
