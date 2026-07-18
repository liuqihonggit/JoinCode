namespace Core.Agents.Doctor;

/// <summary>
/// 修复引擎 — 根据 DiagnosticReport 执行修复流程
/// 支持 1:N 多病人：所有操作通过 patientId 指定目标病人
/// </summary>
public sealed class HotFixEngine
{
    private readonly SourceCodePatcher _patcher;
    private readonly BuildOrchestrator _builder;
    private readonly PatientProcessManager _patientManager;
    private readonly IDoctorTransport _transport;
    private readonly IFileSystem _fs;
    private readonly List<HotFixResult> _results = [];
    private readonly SemaphoreSlim _resultLock = new(1, 1);

    public IReadOnlyList<HotFixResult> Results
    {
        get
        {
            if (_resultLock.Wait(0))
            {
                try { return _results.ToList(); }
                finally { _resultLock.Release(); }
            }
            return _results.ToList();
        }
    }

    public event EventHandler<HotFixResult>? FixApplied;
    public event EventHandler<HotFixResult>? FixRolledBack;

    public HotFixEngine(
        SourceCodePatcher patcher,
        BuildOrchestrator builder,
        PatientProcessManager patientManager,
        IDoctorTransport transport,
        IFileSystem fs)
    {
        _patcher = patcher ?? throw new ArgumentNullException(nameof(patcher));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _patientManager = patientManager ?? throw new ArgumentNullException(nameof(patientManager));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    public async Task<HotFixResult> ExecuteFixAsync(
        DiagnosticReport report,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var action = ChooseAction(report);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        DoctorDiag.Write($"[Doctor] 执行修复: {action.ActionType} - {action.Description} (病人: {report.PatientId})");

        var result = action.ActionType switch
        {
            HotFixActionType.SourceCodePatch => await ExecuteSourceCodePatchAsync(report.PatientId, action, workingDirectory, cancellationToken).ConfigureAwait(false),
            HotFixActionType.ConfigChange => await ExecuteConfigChangeAsync(action, workingDirectory, cancellationToken).ConfigureAwait(false),
            HotFixActionType.CompactContext => await ExecuteCompactContextAsync(report.PatientId, action, cancellationToken).ConfigureAwait(false),
            HotFixActionType.RestartProcess => await ExecuteRestartProcessAsync(report.PatientId, action, workingDirectory, cancellationToken).ConfigureAwait(false),
            _ => CreateNoOpResult(action, report.PatientId, sw.Elapsed)
        };

        sw.Stop();
        result = result with { Duration = sw.Elapsed, PatientId = report.PatientId };

        await _resultLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _results.Add(result); }
        finally { _resultLock.Release(); }

        if (result.Success)
        {
            DoctorDiag.Write($"[Doctor] 修复成功: {action.ActionType} (病人: {report.PatientId})");
            FixApplied?.Invoke(this, result);
        }
        else
        {
            DoctorDiag.WriteError($"[Doctor] 修复失败: {action.ActionType} - {result.Description} (病人: {report.PatientId})");
            if (result.WasRolledBack)
            {
                FixRolledBack?.Invoke(this, result);
            }
        }

        return result;
    }

    internal HotFixAction ChooseAction(DiagnosticReport report)
    {
        return report.SuggestedFixType switch
        {
            HotFixActionType.SourceCodePatch => new HotFixAction
            {
                ActionType = HotFixActionType.SourceCodePatch,
                Description = report.SuggestedFixDescription ?? "修改源码并重编译",
                TargetFilePath = InferTargetFilePath(report)
            },
            HotFixActionType.ConfigChange => BuildConfigChangeAction(report),
            HotFixActionType.CompactContext => new HotFixAction
            {
                ActionType = HotFixActionType.CompactContext,
                Description = report.SuggestedFixDescription ?? "压缩上下文",
                CommandToSend = "/compact"
            },
            HotFixActionType.RestartProcess => new HotFixAction
            {
                ActionType = HotFixActionType.RestartProcess,
                Description = report.SuggestedFixDescription ?? "重启病人进程"
            },
            _ => new HotFixAction
            {
                ActionType = HotFixActionType.None,
                Description = "无需修复"
            }
        };
    }

    private async Task<HotFixResult> ExecuteSourceCodePatchAsync(
        string patientId,
        HotFixAction action,
        string? workingDirectory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(action.TargetFilePath))
        {
            return new HotFixResult { Success = false, PatientId = patientId, Action = action, Description = "未指定目标文件路径" };
        }

        if (string.IsNullOrWhiteSpace(action.PatchedContent))
        {
            DoctorDiag.Write($"[Doctor] 源码修复需要人工介入: {action.TargetFilePath} (病人: {patientId})");
            return new HotFixResult { Success = false, PatientId = patientId, Action = action, Description = $"源码修复需要人工介入，目标文件: {action.TargetFilePath ?? "未知"}" };
        }

        var patchResult = await _patcher.ApplyPatchAsync(
            action.TargetFilePath,
            action.OriginalContent ?? "",
            action.PatchedContent,
            ct).ConfigureAwait(false);

        if (!patchResult.Success)
        {
            return new HotFixResult { Success = false, PatientId = patientId, Action = action, Description = patchResult.Description };
        }

        var buildResult = await _builder.BuildProjectAsync(
            action.TargetFilePath,
            "Debug",
            workingDirectory,
            ct).ConfigureAwait(false);

        if (!buildResult.Success)
        {
            DoctorDiag.WriteError($"[Doctor] 编译失败，回滚源码修改: {action.TargetFilePath}");

            var rollbackResult = await _patcher.RollbackAsync(
                action.TargetFilePath,
                patchResult.OriginalContent ?? action.OriginalContent ?? "",
                ct).ConfigureAwait(false);

            return new HotFixResult
            {
                Success = false,
                PatientId = patientId,
                Action = action,
                Description = $"编译失败（退出码 {buildResult.ExitCode}），已{(rollbackResult.Success ? "回滚成功" : "回滚失败")}",
                WasRolledBack = rollbackResult.Success,
                BuildOutput = buildResult.StandardOutput
            };
        }

        var patientInfo = _patientManager.GetPatientInfo(patientId);
        var originalArgs = patientInfo?.Arguments;

        await _patientManager.KillAsync(patientId).ConfigureAwait(false);
        await _patientManager.RemovePatientAsync(patientId).ConfigureAwait(false);

        if (originalArgs is not null)
        {
            try
            {
                await _patientManager.SpawnAsync(
                    patientId,
                    originalArgs,
                    workingDirectory,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new HotFixResult { Success = false, PatientId = patientId, Action = action, Description = $"重启病人进程失败: {ex.Message}" };
            }
        }

        return new HotFixResult
        {
            Success = true,
            PatientId = patientId,
            Action = action,
            Description = "源码修改成功，编译通过，病人已重启",
            BuildOutput = buildResult.StandardOutput
        };
    }

    private async Task<HotFixResult> ExecuteConfigChangeAsync(
        HotFixAction action,
        string? workingDirectory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(action.TargetFilePath))
        {
            return new HotFixResult { Success = false, Action = action, Description = "未指定配置文件路径，配置修复需要人工介入" };
        }

        if (string.IsNullOrWhiteSpace(action.PatchedContent))
        {
            return new HotFixResult { Success = false, Action = action, Description = "未指定配置内容，配置修复需要人工介入" };
        }

        var patchResult = await _patcher.ApplyPatchAsync(
            action.TargetFilePath,
            action.OriginalContent ?? "",
            action.PatchedContent,
            ct).ConfigureAwait(false);

        if (!patchResult.Success)
        {
            return new HotFixResult { Success = false, Action = action, Description = patchResult.Description };
        }

        DoctorDiag.Write($"[Doctor] 配置文件已修改，验证热更新: {action.TargetFilePath}");

        var verified = await VerifyConfigChangeAsync(action.TargetFilePath, ct).ConfigureAwait(false);

        return new HotFixResult
        {
            Success = true,
            Action = action,
            Description = verified
                ? $"配置文件已修改并验证: {action.TargetFilePath}"
                : $"配置文件已修改: {action.TargetFilePath}，热更新验证超时（可能需要重启）"
        };
    }

    private async Task<HotFixResult> ExecuteCompactContextAsync(
        string patientId,
        HotFixAction action,
        CancellationToken ct)
    {
        var command = action.CommandToSend ?? "/compact";

        try
        {
            await _transport.SendCommandAsync(patientId, command + Environment.NewLine, ct).ConfigureAwait(false);

            DoctorDiag.Write($"[Doctor] 已发送压缩指令到病人 {patientId}: {command}");

            await Task.Delay(2000, ct).ConfigureAwait(false);

            return new HotFixResult
            {
                Success = true,
                PatientId = patientId,
                Action = action,
                Description = $"已发送 {command} 指令给病人 {patientId}"
            };
        }
        catch (Exception ex)
        {
            return new HotFixResult
            {
                Success = false,
                PatientId = patientId,
                Action = action,
                Description = $"发送压缩指令失败: {ex.Message}"
            };
        }
    }

    private async Task<HotFixResult> ExecuteRestartProcessAsync(
        string patientId,
        HotFixAction action,
        string? workingDirectory,
        CancellationToken ct)
    {
        var patientInfo = _patientManager.GetPatientInfo(patientId);
        var originalArgs = patientInfo?.Arguments;

        await _patientManager.KillAsync(patientId).ConfigureAwait(false);
        await _patientManager.RemovePatientAsync(patientId).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(originalArgs))
        {
            return new HotFixResult { Success = false, PatientId = patientId, Action = action, Description = "无法重启：缺少病人进程参数" };
        }

        try
        {
            await _patientManager.SpawnAsync(patientId, originalArgs, workingDirectory, cancellationToken: ct).ConfigureAwait(false);

            return new HotFixResult
            {
                Success = true,
                PatientId = patientId,
                Action = action,
                Description = $"病人 {patientId} 进程已重启"
            };
        }
        catch (Exception ex)
        {
            return new HotFixResult
            {
                Success = false,
                PatientId = patientId,
                Action = action,
                Description = $"重启病人进程失败: {ex.Message}"
            };
        }
    }

    private static HotFixResult CreateNoOpResult(HotFixAction action, string patientId, TimeSpan duration)
    {
        return new HotFixResult
        {
            Success = true,
            PatientId = patientId,
            Action = action,
            Description = "无需修复",
            Duration = duration
        };
    }

    private static string? InferTargetFilePath(DiagnosticReport report)
    {
        if (report.TriggeringEvents.Count == 0)
            return null;

        foreach (var evt in report.TriggeringEvents)
        {
            if (evt.Properties.TryGetValue("source_file", out var file) && !string.IsNullOrWhiteSpace(file))
                return file;

            if (evt.Properties.TryGetValue("file_path", out var path) && !string.IsNullOrWhiteSpace(path))
                return path;

            if (evt.Properties.TryGetValue("source", out var source) && !string.IsNullOrWhiteSpace(source))
                return source;
        }

        if (report.RuleId == DiagnosticRuleId.LoopDetected)
        {
            return InferLoopTargetFile(report);
        }

        return null;
    }

    private static string? InferLoopTargetFile(DiagnosticReport report)
    {
        foreach (var evt in report.TriggeringEvents)
        {
            if (evt.Properties.TryGetValue("tool", out var tool) && !string.IsNullOrWhiteSpace(tool))
            {
                var fileName = tool switch
                {
                    "Read" or "read_file" => "FileReadMiddleware.cs",
                    "Edit" or "write_file" or "file_edit" => "FileEditMiddleware.cs",
                    "Bash" or "shell_exec" => "ShellExecMiddleware.cs",
                    "Grep" or "search_code" => "CodeSearchMiddleware.cs",
                    _ => null
                };

                if (fileName is not null)
                    return fileName;
            }
        }

        return null;
    }

    private static HotFixAction BuildConfigChangeAction(DiagnosticReport report)
    {
        var targetFilePath = InferConfigFilePath(report);

        if (report.RuleId == DiagnosticRuleId.ToolPermissionDenied)
        {
            var toolName = report.TriggeringEvents
                .Select(e => e.Properties.GetValueOrDefault("tool") ?? e.Properties.GetValueOrDefault("tool_name"))
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "unknown";

            return new HotFixAction
            {
                ActionType = HotFixActionType.ConfigChange,
                Description = report.SuggestedFixDescription ?? $"为工具 {toolName} 添加允许权限",
                TargetFilePath = targetFilePath,
                PatchedContent = $"{{\"permissions\": {{\"allowedTools\": [\"{toolName}\"]}}}}"
            };
        }

        if (report.RuleId == DiagnosticRuleId.ToolExecutionError)
        {
            var toolName = report.TriggeringEvents
                .Select(e => e.Properties.GetValueOrDefault("tool") ?? e.Properties.GetValueOrDefault("tool_name"))
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "unknown";

            return new HotFixAction
            {
                ActionType = HotFixActionType.ConfigChange,
                Description = report.SuggestedFixDescription ?? $"检查工具 {toolName} 的配置和依赖",
                TargetFilePath = targetFilePath
            };
        }

        return new HotFixAction
        {
            ActionType = HotFixActionType.ConfigChange,
            Description = report.SuggestedFixDescription ?? "修改配置文件",
            TargetFilePath = targetFilePath
        };
    }

    private async Task<bool> VerifyConfigChangeAsync(string filePath, CancellationToken ct)
    {
        const int maxRetries = 5;
        const int retryDelayMs = 500;

        for (var i = 0; i < maxRetries; i++)
        {
            await Task.Delay(retryDelayMs, ct).ConfigureAwait(false);

            if (!_fs.FileExists(filePath))
                continue;

            try
            {
                var content = await _fs.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    DoctorDiag.Write($"[Doctor] 配置热更新验证成功: {filePath} (第{i + 1}次检查)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                DoctorDiag.WriteError($"[Doctor] 配置热更新验证读取失败: {filePath}: {ex.Message}");
            }
        }

        DoctorDiag.WriteError($"[Doctor] 配置热更新验证超时: {filePath}");
        return false;
    }

    private static string? InferConfigFilePath(DiagnosticReport report)
    {
        if (report.RuleId == DiagnosticRuleId.ToolPermissionDenied
            || report.RuleId == DiagnosticRuleId.ApiError)
        {
            return "settings.json";
        }
        return null;
    }
}
