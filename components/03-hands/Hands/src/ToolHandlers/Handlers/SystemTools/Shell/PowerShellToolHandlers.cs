


namespace Tools.Handlers;

/// <summary>
/// PowerShell 增强命令选项
/// </summary>
public sealed record PowerShellOptions
{
    [McpToolParameter("PowerShell command or script to execute")]
    public required string Command { get; init; }

    [McpToolParameter("Do not load PowerShell profile", Required = false, DefaultValue = "false")]
    public bool? NoProfile { get; init; }

    [McpToolParameter("Execution policy (e.g. Bypass, RemoteSigned, AllSigned)", Required = false)]
    public string? ExecutionPolicy { get; init; }

    [McpToolParameter("Command mode (-Command or -File)", Required = false, DefaultValue = "Command")]
    public string? CommandMode { get; init; }

    [McpToolParameter("Run as administrator", Required = false, DefaultValue = "false")]
    public bool? RunAsAdmin { get; init; }

    [McpToolParameter("Timeout in milliseconds, defaults to 60000", Required = false, DefaultValue = "60000")]
    public int? Timeout { get; init; }

    [McpToolParameter("Working directory, defaults to current directory", Required = false)]
    public string? WorkingDirectory { get; init; }

    [McpToolParameter("Run in background", Required = false, DefaultValue = "false")]
    public bool? Background { get; init; }
}

/// <summary>
/// PowerShell专用工具处理器 - 提供针对PowerShell优化的功能
/// </summary>
[McpToolHandler(ToolCategory.PowerShell)]
public class PowerShellToolHandlers
{
    private readonly IShellExecutionService _shellExecutionService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly IShellBackgroundTaskService? _backgroundTaskService;
    private readonly ITelemetryService? _telemetryService;
    private readonly IPsPermissionChecker? _psPermissionChecker;
    private readonly IPsDestructiveCommandChecker? _psDestructiveCommandChecker;

    public PowerShellToolHandlers(
        IShellExecutionService shellExecutionService,
        IFileOperationService fileOperationService,
        IFileSystem fs,
        IShellBackgroundTaskService? backgroundTaskService = null,
        ITelemetryService? telemetryService = null,
        IPsPermissionChecker? psPermissionChecker = null,
        IPsDestructiveCommandChecker? psDestructiveCommandChecker = null)
    {
        _shellExecutionService = shellExecutionService ?? throw new ArgumentNullException(nameof(shellExecutionService));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _backgroundTaskService = backgroundTaskService;
        _telemetryService = telemetryService;
        _psPermissionChecker = psPermissionChecker;
        _psDestructiveCommandChecker = psDestructiveCommandChecker;
    }

    /// <summary>
    /// 执行PowerShell命令（增强版）
    /// </summary>
    [McpTool(ShellToolNameConstants.Powershell, "Execute PowerShell command (enhanced, supports more parameters)", "execution")]
    public async Task<ToolResult> PowerShellAsync(
        [McpToolOptions] PowerShellOptions options,
        CancellationToken cancellationToken = default)
    {
        var command = options.Command;
        var no_profile = options.NoProfile;
        var execution_policy = options.ExecutionPolicy;
        var command_mode = options.CommandMode;
        var run_as_admin = options.RunAsAdmin;
        var timeout = options.Timeout;
        var working_directory = options.WorkingDirectory;
        var background = options.Background;

        if (string.IsNullOrWhiteSpace(command))
        {
            return ResultBuilder.Error().WithText("command cannot be empty").Build();
        }

        var workDir = string.IsNullOrEmpty(working_directory) ? _fs.GetCurrentDirectory() : working_directory;
        if (_psPermissionChecker is not null)
        {
            var permResult = _psPermissionChecker.CheckPermission(
                command, workDir, [], [], [], [], [], false);
            if (permResult.Behavior == PermissionBehavior.Deny
                || permResult.Behavior == PermissionBehavior.Ask)
            {
                var permWarning = new StringBuilder();
                permWarning.AppendLine($"{StatusSymbol.Warning.ToValue()} {(permResult.Behavior == PermissionBehavior.Deny ? "Operation denied" : "User approval required")}");
                permWarning.AppendLine();
                if (!string.IsNullOrEmpty(permResult.Message))
                {
                    permWarning.AppendLine(permResult.Message);
                }
                if (!string.IsNullOrEmpty(permResult.Suggestions))
                {
                    permWarning.AppendLine();
                    permWarning.AppendLine(permResult.Suggestions);
                }

                RecordPsmetrics("ps_enhanced", permResult.Behavior == PermissionBehavior.Deny ? "denied" : "ask");
                return ResultBuilder.Error().WithText(permWarning.ToString()).Build();
            }
        }

        if (_psDestructiveCommandChecker is not null)
        {
            var psWarning = _psDestructiveCommandChecker.GetDestructiveCommandWarning(command);
            if (psWarning != null)
            {
                var warning = new StringBuilder();
                warning.AppendLine($"{StatusSymbol.Warning.ToValue()} Potentially dangerous command detected");
                warning.AppendLine();
                warning.AppendLine(psWarning);
                warning.AppendLine();
                warning.AppendLine("If you are sure you want to execute this command, re-invoke and confirm you understand the risks.");

                RecordPsmetrics("ps_enhanced", "dangerous");
                return ResultBuilder.Error().WithText(warning.ToString()).Build();
            }
        }

        // 构建PowerShell参数
        var psArgs = new StringBuilder();

        if (no_profile == true)
        {
            psArgs.Append("-NoProfile ");
        }

        if (!string.IsNullOrEmpty(execution_policy))
        {
            psArgs.Append($"-ExecutionPolicy {execution_policy} ");
        }

        var mode = command_mode?.ToLowerInvariant() == "file" ? "-File" : "-Command";
        psArgs.Append($"{mode} \"{command.Replace("\"", "`\"")}\"");

        var fullCommand = $"powershell.exe {psArgs}";

        // 后台任务模式
        if (background == true && _backgroundTaskService != null)
        {
            var taskInfo = await _backgroundTaskService.CreateTaskAsync(
                fullCommand,
                working_directory,
                cancellationToken).ConfigureAwait(false);

            var response = new StringBuilder();
            response.AppendLine($"{StatusSymbol.Play.ToValue()} PowerShell background task created");
            response.AppendLine($"Task ID: {taskInfo.TaskId}");
            response.AppendLine($"Command: {command[..Math.Min(50, command.Length)]}...");
            response.AppendLine();
            response.AppendLine("Use the following command to check task status:");
            response.AppendLine($"  - shell_background_get task_id=\"{taskInfo.TaskId}\"");

            return ResultBuilder.Success().WithText(response.ToString()).Build();
        }

        var result = await _shellExecutionService.ExecuteAsync(
            fullCommand,
            timeout ?? 60000,
            working_directory,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.Interrupted)
        {
            RecordPsmetrics("ps_enhanced", "interrupted");
            return ResultBuilder.Error().WithText(result.Stderr).Build();
        }

        var output = BuildOutputResponse(result);

        // 使用 PS 命令语义解释退出码（区分 PS 原生 cmdlet 和外部可执行文件）
        if (result.ExitCode.HasValue && result.ExitCode.Value != 0)
        {
            var semantic = JoinCode.Hands.Shell.PsCommandSemantics.InterpretCommandResult(
                command, result.ExitCode.Value, result.Stdout ?? string.Empty, result.Stderr ?? string.Empty);

            if (!semantic.IsError)
            {
                // 退出码非零但语义上不是错误（如 grep 无匹配、robocopy 成功复制）
                RecordPsmetrics("ps_enhanced", "ok");
                if (semantic.Message != null)
                {
                    output = $"{output}\n[{semantic.Message}]";
                }
                return ResultBuilder.Success().WithText(output).Build();
            }
        }

        if (!result.Success)
        {
            RecordPsmetrics("ps_enhanced", "failed");
            if (result.Stderr?.Contains("Cannot invoke method") == true ||
                result.Stderr?.Contains("constrained language") == true)
            {
                output = $"{output}\n\n{StatusSymbol.Warning.ToValue()} Note: Constrained Language Mode (CLM) detected. Some PowerShell features may be unavailable.";
            }

            return ResultBuilder.Error().WithText(output).Build();
        }

        RecordPsmetrics("ps_enhanced", "ok");
        return ResultBuilder.Success().WithText(output).Build();
    }

    /// <summary>
    /// 执行PowerShell脚本文件
    /// </summary>
    [McpTool(ShellToolNameConstants.PowershellScript, "Execute PowerShell script file (.ps1)", "execution")]
    public async Task<ToolResult> PowerShellScriptAsync(
        [McpToolParameter("Script file path")] string script_path,
        [McpToolParameter("Script arguments (optional)", Required = false)] string? arguments = null,
        [McpToolParameter("Do not load PowerShell profile", Required = false, DefaultValue = "true")] bool? no_profile = null,
        [McpToolParameter("Execution policy", Required = false)] string? execution_policy = null,
        [McpToolParameter("Timeout in milliseconds", Required = false, DefaultValue = "60000")] int? timeout = null,
        [McpToolParameter("Working directory", Required = false)] string? working_directory = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(script_path))
        {
            return ResultBuilder.Error().WithText("script_path cannot be empty").Build();
        }

        // 检查文件扩展名
        if (!script_path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return ResultBuilder.Error().WithText("File must be a .ps1 PowerShell script").Build();
        }

        var fileResult = await _fileOperationService.ReadFileAsync(script_path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!fileResult.Success)
        {
            return ResultBuilder.Error().WithText($"Script file does not exist: {script_path}").Build();
        }

        // 构建PowerShell参数
        var psArgs = new StringBuilder();

        if (no_profile != false)
        {
            psArgs.Append("-NoProfile ");
        }

        if (!string.IsNullOrEmpty(execution_policy))
        {
            psArgs.Append($"-ExecutionPolicy {execution_policy} ");
        }

        psArgs.Append($"-File \"{script_path}\"");

        if (!string.IsNullOrEmpty(arguments))
        {
            psArgs.Append($" {arguments}");
        }

        var fullCommand = $"powershell.exe {psArgs}";

        var result = await _shellExecutionService.ExecuteAsync(
            fullCommand,
            timeout ?? 60000,
            working_directory,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.Interrupted)
        {
            RecordPsmetrics("ps_script", "interrupted");
            return ResultBuilder.Error().WithText(result.Stderr).Build();
        }

        var output = BuildOutputResponse(result);

        if (!result.Success)
        {
            RecordPsmetrics("ps_script", "failed");
            return ResultBuilder.Error().WithText(output).Build();
        }

        RecordPsmetrics("ps_script", "ok");
        return ResultBuilder.Success().WithText(output).Build();
    }

    /// <summary>
    /// 获取PowerShell版本信息
    /// </summary>
    [McpTool(ShellToolNameConstants.PowershellVersion, "Get PowerShell version and runtime information", "execution")]
    public async Task<ToolResult> PowerShellVersionAsync(
        CancellationToken cancellationToken = default)
    {
        var command = "$PSVersionTable | ConvertTo-Json";
        var fullCommand = $"powershell.exe -NoProfile -Command \"{command}\"";

        var result = await _shellExecutionService.ExecuteAsync(
            fullCommand,
            10000,
            null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var response = new StringBuilder();
        response.AppendLine($"{ObjectSymbol.List.ToValue()} PowerShell Version Information");
        response.AppendLine();

        if (result.Success && !string.IsNullOrEmpty(result.Stdout))
        {
            response.AppendLine(result.Stdout);
        }
        else
        {
            var simpleResult = await _shellExecutionService.ExecuteAsync(
                "powershell.exe -NoProfile -Command \"$PSVersionTable.PSVersion\"",
                10000,
                null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (simpleResult.Success)
            {
                response.AppendLine($"PowerShell version: {simpleResult.Stdout}");
            }
            else
            {
                response.AppendLine("Unable to get PowerShell version information");
            }
        }

        // 检查CLM状态
        var clmCheck = await CheckConstrainedLanguageModeAsync(cancellationToken).ConfigureAwait(false);
        response.AppendLine();
        response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} Constrained Language Mode (CLM):");
        response.AppendLine(clmCheck.IsConstrained ? "Enabled (restricted)" : "Disabled (full functionality)");

        if (!string.IsNullOrEmpty(clmCheck.Warning))
        {
            response.AppendLine($"{StatusSymbol.Warning.ToValue()} {clmCheck.Warning}");
        }

        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取PowerShell执行策略
    /// </summary>
    [McpTool(ShellToolNameConstants.PowershellExecutionPolicy, "Get current PowerShell execution policy", "execution")]
    public async Task<ToolResult> PowerShellExecutionPolicyAsync(
        [McpToolParameter("Scope (e.g. Process, CurrentUser, LocalMachine)", Required = false)] string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var command = string.IsNullOrEmpty(scope)
            ? "Get-ExecutionPolicy -List | Format-Table -AutoSize"
            : $"Get-ExecutionPolicy -Scope {scope}";

        var fullCommand = $"powershell.exe -NoProfile -Command \"{command}\"";

        var result = await _shellExecutionService.ExecuteAsync(
            fullCommand,
            10000,
            null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var response = new StringBuilder();
        response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} PowerShell Execution Policy");
        response.AppendLine();

        if (!string.IsNullOrEmpty(scope))
        {
            response.AppendLine($"Scope: {scope}");
        }

        if (result.Success)
        {
            response.AppendLine(result.Stdout);
        }
        else
        {
            response.AppendLine($"Failed to get execution policy: {result.Stderr}");
        }

        response.AppendLine();
        response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} Notes:");
        response.AppendLine("  - Restricted: No scripts allowed");
        response.AppendLine("  - AllSigned: Only signed scripts allowed");
        response.AppendLine("  - RemoteSigned: Local scripts allowed, remote scripts must be signed");
        response.AppendLine("  - Unrestricted: All scripts allowed");
        response.AppendLine("  - Bypass: No restrictions");

        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 设置PowerShell执行策略
    /// </summary>
    [McpTool(ShellToolNameConstants.PowershellSetExecutionPolicy, "Set PowerShell execution policy (requires administrator privileges)", "execution")]
    public async Task<ToolResult> PowerShellSetExecutionPolicyAsync(
        [McpToolParameter("Execution policy (e.g. RemoteSigned, Bypass, AllSigned)")] string policy,
        [McpToolParameter("Scope", Required = false, DefaultValue = "Process")] string? scope = null,
        [McpToolParameter("Force setting without confirmation prompt", Required = false, DefaultValue = "true")] bool? force = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(policy))
        {
            return ResultBuilder.Error().WithText("policy cannot be empty").Build();
        }

        var validPolicies = new[] { "Restricted", "AllSigned", "RemoteSigned", "Unrestricted", "Bypass", "Undefined" };
        if (!validPolicies.Contains(policy, StringComparer.OrdinalIgnoreCase))
        {
            return ResultBuilder.Error()
                .WithText($"Invalid execution policy: {policy}. Valid values: {string.Join(", ", validPolicies)}")
                .Build();
        }

        var scopeParam = string.IsNullOrEmpty(scope) ? "Process" : scope;
        var forceParam = force != false ? "-Force" : "";

        var command = $"Set-ExecutionPolicy -ExecutionPolicy {policy} -Scope {scopeParam} {forceParam}";
        var fullCommand = $"powershell.exe -NoProfile -Command \"{command}\"";

        var result = await _shellExecutionService.ExecuteAsync(
            fullCommand,
            30000,
            null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            return ResultBuilder.Success()
                .WithText($"{StatusSymbol.Tick.ToValue()} Execution policy set to '{policy}' (scope: {scopeParam})")
                .Build();
        }
        else
        {
            var error = result.Stderr ?? "Unknown error";

            if (error.Contains("Access is denied") || error.Contains("权限"))
            {
                error = $"{error}\n\n{StatusSymbol.Warning.ToValue()} Administrator privileges are required to change the execution policy for this scope.\nSuggestion: Use scope=\"Process\" to change the execution policy for the current process only.";
            }

            return ResultBuilder.Error().WithText(error).Build();
        }
    }

    #region Private Methods

    private void RecordPsmetrics(string operation, string result)
    {
        if (_telemetryService == null) return;
        var counter = _telemetryService.GetCounter("powershell.handler.count", "count", "PowerShell handler count");
        counter.Add(1, new Dictionary<string, string> { ["operation"] = operation, ["result"] = result });
    }

    private static string BuildOutputResponse(ShellExecutionResult result)
    {
        var response = new StringBuilder();

        if (!string.IsNullOrEmpty(result.Stdout))
        {
            response.AppendLine("[stdout]");
            response.AppendLine(result.Stdout);
        }

        if (!string.IsNullOrEmpty(result.Stderr))
        {
            response.AppendLine("[stderr]");
            response.AppendLine(result.Stderr);
        }

        if (result.ExitCode.HasValue && result.ExitCode.Value != 0)
        {
            response.AppendLine($"[exit code] {result.ExitCode}");
        }

        var output = response.ToString().Trim();
        if (string.IsNullOrEmpty(output))
        {
            output = "Command completed (no output)";
        }

        return output;
    }

    private async Task<ConstrainedLanguageModeCheck> CheckConstrainedLanguageModeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var command = "$ExecutionContext.SessionState.LanguageMode";
            var fullCommand = $"powershell.exe -NoProfile -Command \"{command}\"";

            var result = await _shellExecutionService.ExecuteAsync(
                fullCommand,
                5000,
                null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.Success && result.Stdout?.Contains("ConstrainedLanguage") == true)
            {
                return new ConstrainedLanguageModeCheck
                {
                    IsConstrained = true,
                    Warning = "Currently in constrained language mode. Some PowerShell features (e.g. .NET type access) may be restricted."
                };
            }

            return new ConstrainedLanguageModeCheck { IsConstrained = false };
        }
        catch
        {
            return new ConstrainedLanguageModeCheck { IsConstrained = false };
        }
    }

    private record ConstrainedLanguageModeCheck
    {
        public bool IsConstrained { get; init; }
        public string? Warning { get; init; }
    }

    #endregion
}
