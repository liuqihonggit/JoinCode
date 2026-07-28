namespace Tools.Handlers;

/// <summary>
/// PowerShell 专用工具处理器 — 对齐 TS PowerShellTool
/// 统一走中间件管道，与 ShellToolHandlers 共享验证、后台化、输出格式化逻辑
/// 继承 ShellToolBase 获得 PowerShell 门控、进程看护、压缩标记
/// </summary>
[McpToolHandler(ToolCategory.PowerShell)]
public class PowerShellToolHandlers : ShellToolBase
{
    private readonly MiddlewarePipeline<ShellPipelineContext> _pipeline;
    private readonly IShellExecutionService _shellExecutionService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly ITelemetryService? _telemetryService;
    private readonly IPsPermissionChecker? _psPermissionChecker;
    private readonly IPsDestructiveCommandChecker? _psDestructiveCommandChecker;

    public override string ToolName => ShellToolNameConstants.Powershell;
    public override bool IsPowerShell => true;

    public PowerShellToolHandlers(
        MiddlewarePipeline<ShellPipelineContext> pipeline,
        IShellExecutionService shellExecutionService,
        IFileOperationService fileOperationService,
        IFileSystem fs,
        IShellToolGateService? gateService = null,
        IShellProcessWatchdog? watchdog = null,
        ITelemetryService? telemetryService = null,
        IPsPermissionChecker? psPermissionChecker = null,
        IPsDestructiveCommandChecker? psDestructiveCommandChecker = null)
        : base(gateService, watchdog)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _shellExecutionService = shellExecutionService ?? throw new ArgumentNullException(nameof(shellExecutionService));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _telemetryService = telemetryService;
        _psPermissionChecker = psPermissionChecker;
        _psDestructiveCommandChecker = psDestructiveCommandChecker;
    }

    /// <summary>
    /// 执行 PowerShell 命令 — 对齐 TS PowerShellTool
    /// 统一走中间件管道：验证 → PS权限检查 → 后台判断 → 执行 → 输出格式化
    /// </summary>
    [McpTool(ShellToolNameConstants.Powershell, "Execute a PowerShell command. The description parameter briefly describes the command purpose", "execution")]
    public async Task<ToolResult> PowerShellAsync(
        [McpToolParameter("PowerShell command to execute")] string command,
        [McpToolParameter("Brief description of the command purpose", Required = false)] string? description = null,
        [McpToolParameter("Timeout in milliseconds, default 120000ms", Required = false, DefaultValue = "120000")] int? timeout = null,
        [McpToolParameter("Working directory, defaults to current directory", Required = false)] string? working_directory = null,
        [McpToolParameter("Run in background (do not wait for completion)", Required = false, DefaultValue = "false")] bool? background = null,
        [McpToolParameter("Enable auto-backgrounding on timeout", Required = false, DefaultValue = "true")] bool? auto_background = null,
        [McpToolParameter("Override sandbox mode for this command", Required = false, DefaultValue = "false")] bool? dangerously_disable_sandbox = null,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        var gateResult = CheckGate(isPowerShellCall: true);
        if (gateResult is not null) return gateResult;

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
                if (!string.IsNullOrEmpty(permResult.Message)) permWarning.AppendLine(permResult.Message);
                if (!string.IsNullOrEmpty(permResult.Suggestions)) { permWarning.AppendLine(); permWarning.AppendLine(permResult.Suggestions); }

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

        var context = new ShellPipelineContext
        {
            Command = command,
            IsPowerShell = true,
            Description = description,
            Timeout = timeout,
            WorkingDirectory = working_directory,
            Background = background,
            AutoBackground = auto_background,
            DangerouslyDisableSandbox = dangerously_disable_sandbox,
            CancellationToken = cancellationToken,
            OnProgress = onProgress,
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result ?? ResultBuilder.Error().WithText("Pipeline did not produce a result").Build();
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
        var gateResult = CheckGate(isPowerShellCall: true);
        if (gateResult is not null) return gateResult;

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

        var output = ShellOutputMiddleware.BuildOutputResponse(result);

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
        var gateResult = CheckGate(isPowerShellCall: true);
        if (gateResult is not null) return gateResult;

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
        var gateResult = CheckGate(isPowerShellCall: true);
        if (gateResult is not null) return gateResult;

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
        var gateResult = CheckGate(isPowerShellCall: true);
        if (gateResult is not null) return gateResult;

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
        => _telemetryService?.RecordCount("powershell.handler.count", new Dictionary<string, string> { ["operation"] = operation, ["result"] = result }, description: "PowerShell handler count");

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
