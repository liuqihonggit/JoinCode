namespace Tools.Handlers;

/// <summary>
/// PowerShell 专用工具处理器 — 对齐 TS PowerShellTool
/// 统一走中间件管道，与 ShellToolHandlers 共享验证、后台化、输出格式化逻辑
/// 继承 ShellToolBase 获得 PowerShell 门控、进程看护、压缩标记
/// </summary>
[McpToolDispatch(ToolCategory.PowerShell)]
public class PowerShellToolHandlers : ShellToolBase
{
    private readonly MiddlewarePipeline<ShellPipelineContext> _pipeline;
    private readonly ISystemActuatorRegistry _registry;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IPsPermissionChecker? _psPermissionChecker;
    private readonly IPsDestructiveCommandChecker? _psDestructiveCommandChecker;

    public override string ToolName => ShellToolNameConstants.Powershell;

    public PowerShellToolHandlers(
        MiddlewarePipeline<ShellPipelineContext> pipeline,
        ISystemActuatorRegistry registry,
        IFileOperationService fileOperationService,
        IFileSystem fs,
        ILogger? logger = null,
        IShellToolGateService? gateService = null,
        IShellProcessWatchdog? watchdog = null,
        ITelemetryService? telemetryService = null,
        IPsPermissionChecker? psPermissionChecker = null,
        IPsDestructiveCommandChecker? psDestructiveCommandChecker = null)
        : base(gateService, watchdog)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
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
        try
        {
            var gateResult = CheckGate(SystemActuatorKind.PowerShell);
            if (gateResult is not null) return gateResult;

            if (string.IsNullOrWhiteSpace(command))
            {
                var diag = BuildCommandEmptyDiagnostic();
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
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
                    var permDiag = BuildPermissionDeniedDiagnostic(permResult, permWarning.ToString());
                    return ToolResultBuilder.Error().WithText(permDiag.FormattedMessage).WithDiagnostic(permDiag).Build();
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
                    var dangerDiag = BuildDestructiveCommandDiagnostic(command, psWarning, warning.ToString());
                    return ToolResultBuilder.Error().WithText(dangerDiag.FormattedMessage).WithDiagnostic(dangerDiag).Build();
                }
            }

            var actuator = _registry.Get(SystemActuatorKind.PowerShell);

            var context = new ShellPipelineContext
            {
                Command = command,
                Provider = actuator,
                Description = description,
                Timeout = timeout,
                TimeoutPolicy = TimeoutPolicy,
                WorkingDirectory = working_directory,
                Background = background,
                AutoBackground = auto_background,
                DangerouslyDisableSandbox = dangerously_disable_sandbox,
                CancellationToken = cancellationToken,
                OnProgress = onProgress,
            };

            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            return context.Result ?? ToolResultBuilder.PipelineNoResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("powershell", ex, _logger, "command", command);
        }
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
        try
        {
            var gateResult = CheckGate(SystemActuatorKind.PowerShell);
            if (gateResult is not null) return gateResult;

            if (string.IsNullOrWhiteSpace(script_path))
            {
                var diag = BuildScriptPathEmptyDiagnostic();
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }

            // 检查文件扩展名
            if (!script_path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                var extDiag = BuildInvalidScriptExtensionDiagnostic(script_path);
                return ToolResultBuilder.Error().WithText(extDiag.FormattedMessage).WithDiagnostic(extDiag).Build();
            }

            var fileResult = await _fileOperationService.ReadFileAsync(script_path, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!fileResult.Success)
            {
                var nfDiag = BuildScriptNotFoundDiagnostic(script_path);
                return ToolResultBuilder.Error().WithText(nfDiag.FormattedMessage).WithDiagnostic(nfDiag).Build();
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

            var result = await _registry.Get(SystemActuatorKind.PowerShell).ExecuteAsync(
                fullCommand,
                timeout ?? 60000,
                working_directory,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.Interrupted)
            {
                RecordPsmetrics("ps_script", "interrupted");
                var intDiag = BuildScriptInterruptedDiagnostic(script_path, result.Stderr);
                return ToolResultBuilder.Error().WithText(intDiag.FormattedMessage).WithDiagnostic(intDiag).Build();
            }

            var output = ShellOutputMiddleware.BuildOutputResponse(result);

            if (!result.Success)
            {
                RecordPsmetrics("ps_script", "failed");
                var failDiag = BuildScriptFailedDiagnostic(script_path, output);
                return ToolResultBuilder.Error().WithText(failDiag.FormattedMessage).WithDiagnostic(failDiag).Build();
            }

            RecordPsmetrics("ps_script", "ok");
            return ToolResultBuilder.Success().WithText(output).Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("powershell_script", ex, _logger, "script_path", script_path ?? "(null)");
        }
    }

    /// <summary>
    /// 获取PowerShell版本信息
    /// </summary>
    [McpTool(ShellToolNameConstants.PowershellVersion, "Get PowerShell version and runtime information", "execution", ConcurrencySafe = true)]
    public async Task<ToolResult> PowerShellVersionAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var gateResult = CheckGate(SystemActuatorKind.PowerShell);
            if (gateResult is not null) return gateResult;

            var command = "$PSVersionTable | ConvertTo-Json";
            var fullCommand = $"powershell.exe -NoProfile -Command \"{command}\"";

            var result = await _registry.Get(SystemActuatorKind.PowerShell).ExecuteAsync(
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
                var simpleResult = await _registry.Get(SystemActuatorKind.PowerShell).ExecuteAsync(
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

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("powershell_version", ex, _logger);
        }
    }

    /// <summary>
    /// 获取PowerShell执行策略
    /// </summary>
    [McpTool(ShellToolNameConstants.PowershellExecutionPolicy, "Get current PowerShell execution policy", "execution", ConcurrencySafe = true)]
    public async Task<ToolResult> PowerShellExecutionPolicyAsync(
        [McpToolParameter("Scope (e.g. Process, CurrentUser, LocalMachine)", Required = false)] string? scope = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var gateResult = CheckGate(SystemActuatorKind.PowerShell);
            if (gateResult is not null) return gateResult;

            var command = string.IsNullOrEmpty(scope)
                ? "Get-ExecutionPolicy -List | Format-Table -AutoSize"
                : $"Get-ExecutionPolicy -Scope {scope}";

            var fullCommand = $"powershell.exe -NoProfile -Command \"{command}\"";

            var result = await _registry.Get(SystemActuatorKind.PowerShell).ExecuteAsync(
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

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("powershell_execution_policy", ex, _logger, "scope", scope ?? "(default)");
        }
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
        try
        {
            var gateResult = CheckGate(SystemActuatorKind.PowerShell);
            if (gateResult is not null) return gateResult;

            if (string.IsNullOrWhiteSpace(policy))
            {
                var diag = BuildPolicyEmptyDiagnostic();
                return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
            }

            var validPolicies = new[] { "Restricted", "AllSigned", "RemoteSigned", "Unrestricted", "Bypass", "Undefined" };
            if (!validPolicies.Contains(policy, StringComparer.OrdinalIgnoreCase))
            {
                var invDiag = BuildInvalidPolicyDiagnostic(policy, validPolicies);
                return ToolResultBuilder.Error()
                    .WithText(invDiag.FormattedMessage)
                    .WithDiagnostic(invDiag)
                    .Build();
            }

            var scopeParam = string.IsNullOrEmpty(scope) ? "Process" : scope;
            var forceParam = force != false ? "-Force" : "";

            var command = $"Set-ExecutionPolicy -ExecutionPolicy {policy} -Scope {scopeParam} {forceParam}";
            var fullCommand = $"powershell.exe -NoProfile -Command \"{command}\"";

            var result = await _registry.Get(SystemActuatorKind.PowerShell).ExecuteAsync(
                fullCommand,
                30000,
                null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                return ToolResultBuilder.Success()
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

                var setFailDiag = BuildSetExecutionPolicyFailedDiagnostic(policy, scopeParam, error);
                return ToolResultBuilder.Error().WithText(setFailDiag.FormattedMessage).WithDiagnostic(setFailDiag).Build();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("powershell_set_execution_policy", ex, _logger, "policy", policy ?? "(null)", "scope", scope ?? "(default)");
        }
    }

    #region Diagnostic Builders

    internal static ToolDiagnostic BuildCommandEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellCommandEmpty",
            formattedMessage: "command cannot be empty",
            details:
            [
                new DiagnosticDetail("Field", "command"),
                new DiagnosticDetail("Requirement", "Non-empty command string"),
            ],
            suggestions:
            [
                "Provide a valid PowerShell command to execute.",
            ]);
    }

    internal static ToolDiagnostic BuildPermissionDeniedDiagnostic(
        PsSecurityResult permResult, string formattedMessage)
    {
        var behavior = permResult.Behavior == PermissionBehavior.Deny ? "Denied" : "Ask";
        return ToolDiagnostic.Create(
            reason: $"PowerShellPermission{behavior}",
            formattedMessage: formattedMessage,
            details:
            [
                new DiagnosticDetail("Behavior", behavior),
                new DiagnosticDetail("Message", permResult.Message ?? "(empty)"),
                new DiagnosticDetail("Suggestions", permResult.Suggestions ?? "(empty)"),
            ],
            suggestions:
            [
                "Review the permission policy configuration.",
                "Adjust the command to comply with permission rules.",
            ]);
    }

    internal static ToolDiagnostic BuildDestructiveCommandDiagnostic(
        string command, string psWarning, string formattedMessage)
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellDestructiveCommand",
            formattedMessage: formattedMessage,
            details:
            [
                new DiagnosticDetail("Command", command),
                new DiagnosticDetail("Warning", psWarning),
            ],
            suggestions:
            [
                "If you are sure you want to execute this command, re-invoke and confirm you understand the risks.",
                "Consider using a less destructive alternative.",
            ]);
    }

    internal static ToolDiagnostic BuildScriptPathEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellScriptPathEmpty",
            formattedMessage: "script_path cannot be empty",
            details:
            [
                new DiagnosticDetail("Field", "script_path"),
                new DiagnosticDetail("Requirement", "Non-empty script file path"),
            ],
            suggestions:
            [
                "Provide a valid .ps1 script file path.",
            ]);
    }

    internal static ToolDiagnostic BuildInvalidScriptExtensionDiagnostic(string scriptPath)
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellInvalidScriptExtension",
            formattedMessage: "File must be a .ps1 PowerShell script",
            details:
            [
                new DiagnosticDetail("ProvidedPath", scriptPath),
                new DiagnosticDetail("RequiredExtension", ".ps1"),
            ],
            suggestions:
            [
                "Ensure the file has a .ps1 extension.",
            ]);
    }

    internal static ToolDiagnostic BuildScriptNotFoundDiagnostic(string scriptPath)
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellScriptNotFound",
            formattedMessage: $"Script file does not exist: {scriptPath}",
            details:
            [
                new DiagnosticDetail("ScriptPath", scriptPath),
            ],
            suggestions:
            [
                "Verify the file path is correct.",
                "Ensure the script file exists at the specified location.",
            ]);
    }

    internal static ToolDiagnostic BuildScriptInterruptedDiagnostic(
        string scriptPath, string stderr)
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellScriptInterrupted",
            formattedMessage: stderr,
            details:
            [
                new DiagnosticDetail("ScriptPath", scriptPath),
                new DiagnosticDetail("Stderr", stderr),
            ],
            suggestions:
            [
                "Check if the script was terminated by a signal or timeout.",
                "Review the script for infinite loops or blocking operations.",
            ]);
    }

    internal static ToolDiagnostic BuildScriptFailedDiagnostic(
        string scriptPath, string output)
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellScriptFailed",
            formattedMessage: output,
            details:
            [
                new DiagnosticDetail("ScriptPath", scriptPath),
                new DiagnosticDetail("Output", output),
            ],
            suggestions:
            [
                "Review the script output for error details.",
                "Check the script syntax and runtime errors.",
            ]);
    }

    internal static ToolDiagnostic BuildPolicyEmptyDiagnostic()
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellPolicyEmpty",
            formattedMessage: "policy cannot be empty",
            details:
            [
                new DiagnosticDetail("Field", "policy"),
                new DiagnosticDetail("ValidValues", "Restricted, AllSigned, RemoteSigned, Unrestricted, Bypass, Undefined"),
            ],
            suggestions:
            [
                "Provide a valid execution policy name.",
            ]);
    }

    internal static ToolDiagnostic BuildInvalidPolicyDiagnostic(
        string policy, string[] validPolicies)
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellInvalidPolicy",
            formattedMessage: $"Invalid execution policy: {policy}. Valid values: {string.Join(", ", validPolicies)}",
            details:
            [
                new DiagnosticDetail("ProvidedPolicy", policy),
                new DiagnosticDetail("ValidValues", string.Join(", ", validPolicies)),
            ],
            suggestions:
            [
                "Choose one of the valid execution policy values.",
            ]);
    }

    internal static ToolDiagnostic BuildSetExecutionPolicyFailedDiagnostic(
        string policy, string scope, string error)
    {
        return ToolDiagnostic.Create(
            reason: "PowerShellSetExecutionPolicyFailed",
            formattedMessage: error,
            details:
            [
                new DiagnosticDetail("Policy", policy),
                new DiagnosticDetail("Scope", scope),
                new DiagnosticDetail("Error", error),
            ],
            suggestions:
            [
                "Use scope=\"Process\" to change the execution policy for the current process only.",
                "Run as administrator for system-wide scope changes.",
            ]);
    }

    #endregion

    private void RecordPsmetrics(string operation, string result)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "powershell.handler.count", operation, result);

    private async Task<ConstrainedLanguageModeCheck> CheckConstrainedLanguageModeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var command = "$ExecutionContext.SessionState.LanguageMode";
            var fullCommand = $"powershell.exe -NoProfile -Command \"{command}\"";

            var result = await _registry.Get(SystemActuatorKind.PowerShell).ExecuteAsync(
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
}
