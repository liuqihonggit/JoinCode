namespace McpToolDispatch;


[McpToolDispatch(ToolCategory.Sandbox)]
public sealed class SandboxToolHandlers
{
    private readonly ISandboxManager _sandboxManager;

    public SandboxToolHandlers(ISandboxManager sandboxManager)
    {
        _sandboxManager = sandboxManager ?? throw new ArgumentNullException(nameof(sandboxManager));
    }

    [McpTool(SandboxToolNameConstants.SandboxEnter, "Enter a sandbox with the specified isolation type. If the requested type is unavailable, automatically falls back to a lower isolation level. Available types: soft (path redirection), process (OS-level process isolation), docker (container isolation), bubblewrap (Linux namespace isolation).", "sandbox")]
    public async Task<ToolResult> SandboxEnterAsync(
        [McpToolParameter("Sandbox type: soft, process, docker, or bubblewrap", Required = true, EnumValues = new[] { SandboxTypeConstants.Soft, SandboxTypeConstants.Process, SandboxTypeConstants.Docker, SandboxTypeConstants.Bubblewrap })] string sandboxType,
        [McpToolParameter("Restrict file system access", Required = false, DefaultValue = "true")] string restrictFileSystem,
        [McpToolParameter("Restrict network access", Required = false, DefaultValue = "true")] string restrictNetwork,
        [McpToolParameter("Custom sandbox root path", Required = false)] string? sandboxRoot,
        [McpToolParameter("Memory limit in MB (process/docker only)", Required = false, DefaultValue = "0")] string memoryLimitMb,
        [McpToolParameter("CPU limit percent 1-100 (process/docker only)", Required = false, DefaultValue = "0")] string cpuLimitPercent,
        [McpToolParameter("Allow automatic fallback to lower isolation if requested type unavailable", Required = false, DefaultValue = "true")] string allowFallback,
        CancellationToken cancellationToken = default)
    {
        var type = SandboxTypeExtensions.FromValue(sandboxType);
        if (type is null)
        {
            return ToolResultBuilder.Error()
                .WithText($"未知沙箱类型: '{sandboxType}'。可用类型: soft, process, docker, bubblewrap。使用 sandbox_status 查看当前平台支持哪些类型。")
                .Build();
        }

        if (!_sandboxManager.AvailableTypes.Contains(type.Value))
        {
            if (!allowFallback.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return ToolResultBuilder.Error()
                    .WithText($"沙箱类型 '{sandboxType}' 在当前平台不可用。可用类型: {string.Join(", ", _sandboxManager.AvailableTypes.Select(t => t.ToValue()))}。设置 allowFallback=true 可自动降级。")
                    .Build();
            }

            return await EnterWithFallbackAsync(type.Value, restrictFileSystem, restrictNetwork, sandboxRoot, memoryLimitMb, cpuLimitPercent, cancellationToken).ConfigureAwait(false);
        }

        var options = new SandboxOptions
        {
            Type = type.Value,
            RestrictFileSystem = restrictFileSystem.Equals("true", StringComparison.OrdinalIgnoreCase),
            RestrictNetwork = restrictNetwork.Equals("true", StringComparison.OrdinalIgnoreCase),
            SandboxRoot = sandboxRoot,
            MemoryLimitMb = int.TryParse(memoryLimitMb, out var mem) ? mem : 0,
            CpuLimitPercent = int.TryParse(cpuLimitPercent, out var cpu) ? cpu : 0
        };

        try
        {
            var info = await _sandboxManager.EnterSandboxAsync(options, cancellationToken).ConfigureAwait(false);

            var response = new StringBuilder();
            response.AppendLine($"Sandbox activated: {info.Type.ToValue()}");
            response.AppendLine($"Sandbox ID: {info.SandboxId}");
            response.AppendLine($"Root path: {info.RootPath}");
            response.AppendLine($"Restricted: {info.IsRestricted}");
            response.AppendLine($"Capabilities: {info.Capabilities}");
            response.AppendLine();
            response.AppendLine("提示: 使用 sandbox_status 查看状态, sandbox_switch 切换类型, sandbox_exit 退出沙箱");
            response.AppendLine("执行命令: 使用 sandbox_exec 在沙箱内执行命令（支持超时防卡死）");

            return ToolResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        }
        catch (Exception ex)
        {
            if (allowFallback.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return await EnterWithFallbackAsync(type.Value, restrictFileSystem, restrictNetwork, sandboxRoot, memoryLimitMb, cpuLimitPercent, cancellationToken).ConfigureAwait(false);
            }

            return ToolResultBuilder.Error()
                .WithText($"进入沙箱失败: {ex.Message}。可用类型: {string.Join(", ", _sandboxManager.AvailableTypes.Select(t => t.ToValue()))}。设置 allowFallback=true 可自动降级。")
                .Build();
        }
    }

    [McpTool(SandboxToolNameConstants.SandboxExit, "Exit the current sandbox and restore normal access.", "sandbox")]
    public async Task<ToolResult> SandboxExitAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_sandboxManager.IsInSandbox)
        {
            return ToolResultBuilder.Success()
                .WithText("当前不在沙箱中，无需退出。使用 sandbox_enter 进入沙箱。")
                .Build();
        }

        try
        {
            var previousType = _sandboxManager.ActiveSandboxType;
            var previousId = _sandboxManager.CurrentSandboxId;
            await _sandboxManager.ExitSandboxAsync(cancellationToken).ConfigureAwait(false);

            return ToolResultBuilder.Success()
                .WithText($"已退出沙箱 (类型: {previousType.ToValue()}, ID: {previousId})。文件系统和网络访问已恢复正常。")
                .Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error()
                .WithText($"退出沙箱失败: {ex.Message}。沙箱资源可能未完全清理，建议使用 sandbox_status 检查状态。")
                .Build();
        }
    }

    [McpTool(SandboxToolNameConstants.SandboxSwitch, "Switch to a different sandbox type while preserving isolation settings. Useful for escalating or de-escalating isolation level. If the target type is unavailable, automatically falls back.", "sandbox")]
    public async Task<ToolResult> SandboxSwitchAsync(
        [McpToolParameter("Target sandbox type: soft, process, docker, or bubblewrap", Required = true, EnumValues = new[] { SandboxTypeConstants.Soft, SandboxTypeConstants.Process, SandboxTypeConstants.Docker, SandboxTypeConstants.Bubblewrap })] string sandboxType,
        CancellationToken cancellationToken = default)
    {
        var type = SandboxTypeExtensions.FromValue(sandboxType);
        if (type is null)
        {
            return ToolResultBuilder.Error()
                .WithText($"未知沙箱类型: '{sandboxType}'。可用类型: soft, process, docker, bubblewrap")
                .Build();
        }

        if (!_sandboxManager.IsInSandbox)
        {
            return ToolResultBuilder.Error()
                .WithText($"当前不在沙箱中，无法切换。请先使用 sandbox_enter 进入沙箱。")
                .Build();
        }

        if (!_sandboxManager.AvailableTypes.Contains(type.Value))
        {
            return ToolResultBuilder.Error()
                .WithText($"沙箱类型 '{sandboxType}' 在当前平台不可用。可用类型: {string.Join(", ", _sandboxManager.AvailableTypes.Select(t => t.ToValue()))}")
                .Build();
        }

        try
        {
            var previousType = _sandboxManager.ActiveSandboxType;
            await _sandboxManager.SwitchProviderAsync(type.Value, cancellationToken).ConfigureAwait(false);

            var isolationChange = GetIsolationChangeDescription(previousType, type.Value);

            return ToolResultBuilder.Success()
                .WithText($"沙箱已切换: {previousType.ToValue()} → {type.Value.ToValue()}。{isolationChange}")
                .Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error()
                .WithText($"切换沙箱失败: {ex.Message}。旧沙箱可能已销毁，建议使用 sandbox_enter 重新进入。")
                .Build();
        }
    }

    [McpTool(SandboxToolNameConstants.SandboxStatus, "Get the current sandbox status including type, isolation level, available types, and health state.", "sandbox")]
    public Task<ToolResult> SandboxStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new StringBuilder();
        response.AppendLine($"In sandbox: {_sandboxManager.IsInSandbox}");

        if (_sandboxManager.IsInSandbox && _sandboxManager.CurrentSandbox is not null)
        {
            var info = _sandboxManager.CurrentSandbox;
            response.AppendLine($"Type: {info.Type.ToValue()}");
            response.AppendLine($"Sandbox ID: {info.SandboxId}");
            response.AppendLine($"Root path: {info.RootPath}");
            response.AppendLine($"Restricted: {info.IsRestricted}");
            response.AppendLine($"File system restricted: {info.RestrictFileSystem}");
            response.AppendLine($"Network restricted: {info.RestrictNetwork}");
            response.AppendLine($"Capabilities: {info.Capabilities}");
            if (info.AllowedPaths is not null && info.AllowedPaths.Count > 0)
            {
                response.AppendLine($"Allowed paths: {string.Join(", ", info.AllowedPaths)}");
            }
        }
        else
        {
            response.AppendLine("提示: 当前无沙箱保护，所有文件系统和网络访问不受限制。");
            response.AppendLine("使用 sandbox_enter 进入沙箱以获得隔离保护。");
        }

        response.AppendLine($"Available types: {string.Join(", ", _sandboxManager.AvailableTypes.Select(t => t.ToValue()))}");
        response.AppendLine($"Health: {_sandboxManager.HealthState.ToValue()}");

        if (_sandboxManager.HealthState == SandboxHealthState.Fallback)
        {
            response.AppendLine("⚠️ 当前沙箱为降级模式，隔离级别低于请求值，请注意安全风险。");
        }
        else if (_sandboxManager.HealthState == SandboxHealthState.Degraded)
        {
            response.AppendLine("⚠️ 沙箱处于降级状态，部分功能可能异常。建议 sandbox_exit 后重新进入。");
        }

        return Task.FromResult(ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build());
    }

    [McpTool(SandboxToolNameConstants.SandboxExec, "Execute a command inside the sandbox with anti-stuck timeout protection. When timeout is reached, the command is NOT interrupted - instead you (LLM) are asked to decide: continue waiting or force stop. Default timeout is 2 minutes.", "sandbox")]
    public async Task<ToolResult> SandboxExecAsync(
        [McpToolParameter("Command to execute in the sandbox", Required = true)] string command,
        [McpToolParameter("Timeout preset: 2min (default), 4min, 8min, or custom", Required = false, DefaultValue = SandboxExecutionTimeoutConstants.TwoMinutes, EnumValues = new[] { SandboxExecutionTimeoutConstants.TwoMinutes, SandboxExecutionTimeoutConstants.FourMinutes, SandboxExecutionTimeoutConstants.EightMinutes, SandboxExecutionTimeoutConstants.Custom })] string timeout,
        [McpToolParameter("Custom timeout in seconds (only used when timeout=custom)", Required = false, DefaultValue = "0")] string customTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!_sandboxManager.IsInSandbox)
        {
            return ToolResultBuilder.Error()
                .WithText("当前不在沙箱中，无法执行沙箱命令。请先使用 sandbox_enter 进入沙箱，或直接使用 bash/powershell 工具执行。")
                .Build();
        }

        var timeoutPreset = SandboxExecutionTimeoutExtensions.FromValue(timeout);
        if (timeoutPreset is null)
        {
            return ToolResultBuilder.Error()
                .WithText($"未知超时选项: '{timeout}'。可用: 2min, 4min, 8min, custom")
                .Build();
        }

        var execOptions = new SandboxExecutionOptions
        {
            TimeoutPreset = timeoutPreset.Value,
            CustomTimeoutSeconds = int.TryParse(customTimeoutSeconds, out var custom) ? custom : 0
        };

        if (timeoutPreset.Value == SandboxExecutionTimeout.Custom && execOptions.CustomTimeoutSeconds <= 0)
        {
            return ToolResultBuilder.Error()
                .WithText("使用 custom 超时选项时，customTimeoutSeconds 必须大于 0。")
                .Build();
        }

        try
        {
            var result = await _sandboxManager.ExecuteInSandboxAsync(command, execOptions, cancellationToken).ConfigureAwait(false);

            return BuildExecResultResponse(result);
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error()
                .WithText($"沙箱执行失败: {ex.Message}")
                .Build();
        }
    }

    [McpTool(SandboxToolNameConstants.SandboxExecContinue, "Continue a timed-out sandbox execution. Choose to wait longer or force stop the command.", "sandbox")]
    public async Task<ToolResult> SandboxExecContinueAsync(
        [McpToolParameter("Execution ID from sandbox_exec timeout response", Required = true)] string executionId,
        [McpToolParameter("Action: wait (continue waiting for another timeout period) or stop (force kill the process)", Required = true, EnumValues = new[] { SandboxContinueActionConstants.Wait, SandboxContinueActionConstants.Stop })] string action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(executionId))
        {
            return ToolResultBuilder.Error()
                .WithText("缺少 executionId 参数。请使用 sandbox_exec 超时响应中返回的 Execution ID。")
                .Build();
        }

        if (!action.Equals("wait", StringComparison.OrdinalIgnoreCase) && !action.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultBuilder.Error()
                .WithText($"未知操作: '{action}'。可用: wait (继续等待), stop (强行终止)")
                .Build();
        }

        try
        {
            var result = await _sandboxManager.ContinueExecutionAsync(executionId, action, cancellationToken).ConfigureAwait(false);

            return BuildExecResultResponse(result);
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error()
                .WithText($"继续执行失败: {ex.Message}")
                .Build();
        }
    }

    private static ToolResult BuildExecResultResponse(SandboxExecutionResult result)
    {
        var response = new StringBuilder();
        response.AppendLine($"Execution ID: {result.ExecutionId}");
        response.AppendLine($"State: {result.State.ToValue()}");
        response.AppendLine($"Elapsed: {result.Elapsed.TotalSeconds:0.0}s / {result.ConfiguredTimeout?.TotalMinutes:0}min limit");

        switch (result.State)
        {
            case SandboxExecutionState.Completed:
                response.AppendLine($"Exit code: {result.ExitCode}");
                if (!string.IsNullOrEmpty(result.Stdout))
                {
                    response.AppendLine("--- stdout ---");
                    response.AppendLine(result.Stdout);
                }
                if (!string.IsNullOrEmpty(result.Stderr))
                {
                    response.AppendLine("--- stderr ---");
                    response.AppendLine(result.Stderr);
                }
                break;

            case SandboxExecutionState.TimedOut:
                response.AppendLine();
                response.AppendLine("⏱️ 命令执行已超时，但未中断！命令仍在运行中。");
                response.AppendLine(result.GetLlmPrompt());
                response.AppendLine();
                response.AppendLine("已收集的部分输出:");
                if (!string.IsNullOrEmpty(result.Stdout))
                {
                    response.AppendLine("--- stdout (partial) ---");
                    response.AppendLine(result.Stdout);
                }
                if (!string.IsNullOrEmpty(result.Stderr))
                {
                    response.AppendLine("--- stderr (partial) ---");
                    response.AppendLine(result.Stderr);
                }
                break;

            case SandboxExecutionState.ForceStopped:
                response.AppendLine("进程已被强行终止。");
                if (!string.IsNullOrEmpty(result.Stdout))
                {
                    response.AppendLine("--- stdout (before kill) ---");
                    response.AppendLine(result.Stdout);
                }
                if (!string.IsNullOrEmpty(result.Stderr))
                {
                    response.AppendLine("--- stderr (before kill) ---");
                    response.AppendLine(result.Stderr);
                }
                break;

            case SandboxExecutionState.Failed:
                response.AppendLine($"Error: {result.ErrorMessage}");
                if (!string.IsNullOrEmpty(result.Stdout))
                {
                    response.AppendLine("--- stdout ---");
                    response.AppendLine(result.Stdout);
                }
                if (!string.IsNullOrEmpty(result.Stderr))
                {
                    response.AppendLine("--- stderr ---");
                    response.AppendLine(result.Stderr);
                }
                break;
        }

        return ToolResultBuilder.Success()
            .WithText(response.ToString())
            .Build();
    }

    private async Task<ToolResult> EnterWithFallbackAsync(
        SandboxType requestedType,
        string restrictFileSystem,
        string restrictNetwork,
        string? sandboxRoot,
        string memoryLimitMb,
        string cpuLimitPercent,
        CancellationToken ct)
    {
        var options = new SandboxOptions
        {
            Type = requestedType,
            RestrictFileSystem = restrictFileSystem.Equals("true", StringComparison.OrdinalIgnoreCase),
            RestrictNetwork = restrictNetwork.Equals("true", StringComparison.OrdinalIgnoreCase),
            SandboxRoot = sandboxRoot,
            MemoryLimitMb = int.TryParse(memoryLimitMb, out var mem) ? mem : 0,
            CpuLimitPercent = int.TryParse(cpuLimitPercent, out var cpu) ? cpu : 0
        };

        var result = await _sandboxManager.TryEnterWithFallbackAsync(options, ct).ConfigureAwait(false);

        if (result.Info is not null)
        {
            var response = new StringBuilder();
            response.AppendLine($"Sandbox activated: {result.Info.Type.ToValue()}");
            response.AppendLine($"Sandbox ID: {result.Info.SandboxId}");
            response.AppendLine($"Root path: {result.Info.RootPath}");

            if (result.WasDegraded)
            {
                response.AppendLine();
                response.AppendLine($"⚠️ 降级提示: {result.Message}");
                response.AppendLine($"请求类型: {result.RequestedType.ToValue()}, 实际类型: {result.ActualType.ToValue()}");
                response.AppendLine("降级后隔离级别较低，请避免执行高风险操作。");
            }

            return ToolResultBuilder.Success()
                .WithText(response.ToString())
                .Build();
        }

        return ToolResultBuilder.Error()
            .WithText(result.Message ?? "所有沙箱类型均不可用。当前平台无沙箱保护。")
            .Build();
    }

    private static string GetIsolationChangeDescription(SandboxType from, SandboxType to)
    {
        var level = new Dictionary<SandboxType, int>
        {
            [SandboxType.Soft] = 1,
            [SandboxType.Process] = 2,
            [SandboxType.Bubblewrap] = 3,
            [SandboxType.Docker] = 4
        };

        var fromLevel = level.GetValueOrDefault(from, 0);
        var toLevel = level.GetValueOrDefault(to, 0);

        if (toLevel > fromLevel)
        {
            return "隔离级别已提升，安全性增强。";
        }

        if (toLevel < fromLevel)
        {
            return "⚠️ 隔离级别已降低，请注意安全风险。";
        }

        return "隔离级别不变。";
    }
}
