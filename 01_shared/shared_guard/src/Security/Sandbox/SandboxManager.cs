namespace Core.Security.Sandbox;


[Register(typeof(ISandboxManager), ServiceLifetime.Singleton)]
public sealed partial class SandboxManager : ServiceEntity, ISandboxManager, IDisposable
{
    private readonly ConcurrentDictionary<SandboxType, ISandboxProvider> _providers;
    private readonly SandboxLifecycleActor _lifecycleActor;
    private readonly ILogger<SandboxManager>? _logger;
    private readonly IFileSystem _fs;
    private readonly SandboxIpcClient? _ipcClient;
    private readonly ConcurrentDictionary<string, SandboxActiveExecution> _activeExecutions = new();

    public SandboxManager(
        IEnumerable<ISandboxProvider> providers,
        IFileSystem fs,
        SandboxIpcClient? ipcClient = null,
        ILogger<SandboxManager>? logger = null)
    {
        _fs = fs;
        _ipcClient = ipcClient;
        _logger = logger;
        _providers = new ConcurrentDictionary<SandboxType, ISandboxProvider>(
            providers
                .Where(p => p.IsAvailable)
                .ToDictionary(p => p.SandboxType, p => p));

        _lifecycleActor = new SandboxLifecycleActor(_providers, _logger);

        _logger?.LogInformation("[SandboxManager] 可用沙箱类型: {Types}", string.Join(", ", _providers.Keys.Select(k => k.ToValue())));
    }

    /// <summary>
    /// 运行时添加沙箱提供器 — 插件加载时调用(ADR 0098 万物皆插件)
    /// </summary>
    public bool AddProvider(ISandboxProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!provider.IsAvailable) return false;
        var added = _providers.TryAdd(provider.SandboxType, provider);
        if (added)
        {
            _logger?.LogInformation("[SandboxManager] 插件注册沙箱类型: {Type}", provider.SandboxType.ToValue());
        }
        return added;
    }

    /// <summary>
    /// 运行时移除沙箱提供器 — 插件卸载时调用
    /// </summary>
    public bool RemoveProvider(SandboxType type)
    {
        var removed = _providers.TryRemove(type, out _);
        if (removed)
        {
            _logger?.LogInformation("[SandboxManager] 插件移除沙箱类型: {Type}", type.ToValue());
        }
        return removed;
    }

    public ISandboxProvider? ActiveProvider => _lifecycleActor.ActiveProvider;

    public SandboxType ActiveSandboxType => _lifecycleActor.ActiveProvider?.SandboxType ?? SandboxType.None;

    public bool IsInSandbox => _lifecycleActor.IsInSandbox;

    public SandboxInfo? CurrentSandbox => _lifecycleActor.CurrentSandbox;

    public string? CurrentSandboxId => _lifecycleActor.ActiveSandboxId;

    public SandboxHealthState HealthState => _lifecycleActor.HealthState;

    public IEnumerable<SandboxType> AvailableTypes => _providers.Keys;

    public async Task<SandboxInfo> EnterSandboxAsync(SandboxOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return await _lifecycleActor.EnterAsync(options, ct).ConfigureAwait(false);
    }

    public async Task ExitSandboxAsync(CancellationToken ct = default)
    {
        await _lifecycleActor.ExitAsync(ct).ConfigureAwait(false);
    }

    public async Task SwitchProviderAsync(SandboxType type, CancellationToken ct = default)
    {
        await _lifecycleActor.SwitchAsync(type, ct).ConfigureAwait(false);
    }

    public async Task<SandboxDegradationResult> TryEnterWithFallbackAsync(SandboxOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var targetType = options.Type;
        if (targetType == SandboxType.None)
        {
            targetType = SandboxType.Soft;
        }

        if (_providers.TryGetValue(targetType, out var directProvider))
        {
            try
            {
                var info = await EnterSandboxAsync(options, ct).ConfigureAwait(false);
                return new SandboxDegradationResult
                {
                    RequestedType = targetType,
                    ActualType = info.Type,
                    WasDegraded = false,
                    Info = info,
                    Message = null
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[SandboxManager] 请求的沙箱类型 {Type} 创建失败，尝试降级", targetType.ToValue());
            }
        }

        var fallbackOrder = OperatingSystem.IsLinux()
            ? new[] { SandboxType.Bubblewrap, SandboxType.Process, SandboxType.Soft }
            : new[] { SandboxType.Process, SandboxType.Soft };
        foreach (var fallbackType in fallbackOrder)
        {
            if (fallbackType == targetType || !_providers.ContainsKey(fallbackType))
            {
                continue;
            }

            try
            {
                var fallbackOptions = new SandboxOptions
                {
                    Type = fallbackType,
                    RestrictFileSystem = options.RestrictFileSystem,
                    RestrictNetwork = options.RestrictNetwork,
                    AllowedPaths = options.AllowedPaths,
                    SandboxRoot = options.SandboxRoot
                };

                var info = await EnterSandboxAsync(fallbackOptions, ct).ConfigureAwait(false);
                _lifecycleActor.SetHealthState(SandboxHealthState.Fallback);

                return new SandboxDegradationResult
                {
                    RequestedType = targetType,
                    ActualType = info.Type,
                    WasDegraded = true,
                    Info = info,
                    Message = $"请求的沙箱类型 '{targetType.ToValue()}' 不可用或创建失败，已自动降级到 '{info.Type.ToValue()}'。降级后隔离级别较低，请注意安全风险。"
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[SandboxManager] 降级到 {Type} 也失败", fallbackType.ToValue());
            }
        }

        return new SandboxDegradationResult
        {
            RequestedType = targetType,
            ActualType = SandboxType.None,
            WasDegraded = true,
            Info = null,
            Message = $"所有沙箱类型均不可用。请求: {targetType.ToValue()}, 可用: {string.Join(", ", AvailableTypes.Select(t => t.ToValue()))}。当前无沙箱保护，请谨慎操作。"
        };
    }

    public ISandboxProvider? GetProvider(SandboxType type)
    {
        return _providers.TryGetValue(type, out var provider) ? provider : null;
    }

    public string ResolvePath(string path)
    {
        var activeProvider = _lifecycleActor.ActiveProvider;
        var activeSandboxId = _lifecycleActor.ActiveSandboxId;
        if (activeProvider is null || activeSandboxId is null)
        {
            return Path.GetFullPath(path);
        }

        return activeProvider.ResolvePath(path, activeSandboxId);
    }

    public async Task<SandboxInfo> CreateSandboxAsync(SandboxType type, SandboxOptions options, CancellationToken ct = default)
    {
        var (provider, _) = ResolveProviderWithFallback(type);
        var effectiveOptions = new SandboxOptions
        {
            Type = provider.SandboxType,
            SandboxRoot = options.SandboxRoot,
            RestrictNetwork = options.RestrictNetwork,
            RestrictFileSystem = options.RestrictFileSystem,
            AllowedPaths = options.AllowedPaths,
            MemoryLimitMb = options.MemoryLimitMb,
            CpuLimitPercent = options.CpuLimitPercent,
            TimeLimitSeconds = options.TimeLimitSeconds,
            DockerImage = options.DockerImage,
            EnvironmentOverrides = options.EnvironmentOverrides
        };
        return await provider.CreateSandboxAsync(effectiveOptions, ct).ConfigureAwait(false);
    }

    public async Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default)
    {
        foreach (var provider in _providers.Values)
        {
            if (provider.GetSandboxInfo(sandboxId) is not null)
            {
                await provider.DestroySandboxAsync(sandboxId, ct).ConfigureAwait(false);
                return;
            }
        }

        _logger?.LogWarning("[SandboxManager] 沙箱 '{Id}' 不存在于任何 Provider 中", sandboxId);
    }

    public SandboxInfo? GetSandboxInfo(string sandboxId)
    {
        foreach (var provider in _providers.Values)
        {
            var info = provider.GetSandboxInfo(sandboxId);
            if (info is not null)
            {
                return info;
            }
        }

        return null;
    }

    public string ResolvePath(string path, string sandboxId)
    {
        foreach (var provider in _providers.Values)
        {
            if (provider.GetSandboxInfo(sandboxId) is not null)
            {
                return provider.ResolvePath(path, sandboxId);
            }
        }

        _logger?.LogWarning("[SandboxManager] 沙箱 '{Id}' 不存在，返回原路径", sandboxId);
        return Path.GetFullPath(path);
    }

    private (ISandboxProvider Provider, bool FallbackUsed) ResolveProviderWithFallback(SandboxType type)
    {
        if (type == SandboxType.None)
        {
            var envType = Environment.GetEnvironmentVariable(JccEnvVar.SandboxMode.ToValue());
            if (!string.IsNullOrEmpty(envType))
            {
                var parsed = SandboxTypeExtensions.FromValue(envType);
                if (parsed is not null && parsed.Value != SandboxType.None)
                {
                    type = parsed.Value;
                }
            }

            if (type == SandboxType.None)
            {
                type = SandboxType.Soft;
            }
        }

        if (_providers.TryGetValue(type, out var provider))
        {
            return (provider, false);
        }

        _logger?.LogWarning("[SandboxManager] 请求的沙箱类型 '{Type}' 不可用，降级到 Soft", type.ToValue());

        if (_providers.TryGetValue(SandboxType.Soft, out var softProvider))
        {
            return (softProvider, true);
        }

        if (_providers.TryGetValue(SandboxType.Process, out var processProvider))
        {
            return (processProvider, true);
        }

        throw new InvalidOperationException($"[GRD009] 沙箱类型 '{type.ToValue()}' 不可用且无降级选项。可用类型: {string.Join(", ", _providers.Keys.Select(k => k.ToValue()))}");
    }

    public async Task<AbstractionsSandboxExecutionResult> ExecuteInSandboxAsync(string command, SandboxExecutionOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);

        if (_ipcClient is not null && !_ipcClient.IsRunning)
        {
            try
            {
                await _ipcClient.StartAsync(ct: ct).ConfigureAwait(false);

                if (_ipcClient.SatelliteProcessId is int satellitePid && _lifecycleActor.ActiveProvider is ProcessSandboxProvider psp && _lifecycleActor.ActiveSandboxId is not null)
                {
                    if (!psp.TryAssignProcessToJobObject(_lifecycleActor.ActiveSandboxId!, satellitePid))
                    {
                        _logger?.LogWarning("[SandboxManager] 将卫星进程 {Pid} 加入 JobObject 失败", satellitePid);
                    }
                    else
                    {
                        _logger?.LogInformation("[SandboxManager] 卫星进程 {Pid} 已加入 JobObject", satellitePid);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[SandboxManager] 卫星进程启动失败，回退到直接执行");
            }
        }

        if (_ipcClient is not null && _ipcClient.IsRunning)
        {
            return await ExecuteViaIpcAsync(command, options, ct).ConfigureAwait(false);
        }

        return await ExecuteDirectlyAsync(command, options, ct).ConfigureAwait(false);
    }

    private async Task<AbstractionsSandboxExecutionResult> ExecuteViaIpcAsync(string command, SandboxExecutionOptions options, CancellationToken ct)
    {
        var executionId = Guid.NewGuid().ToString("N")[..16];
        var timeoutSeconds = options.GetTimeoutSeconds();
        var configuredTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        var stopwatch = Stopwatch.StartNew();

        var activeProvider = _lifecycleActor.ActiveProvider;
        var activeSandboxId = _lifecycleActor.ActiveSandboxId;

        var workingDir = activeProvider is not null && activeSandboxId is not null
            ? activeProvider.ResolvePath(".", activeSandboxId)
            : _fs.GetCurrentDirectory();

        var envVars = new Dictionary<string, string>();
        if (activeProvider is not null && activeSandboxId is not null)
        {
            var sandboxInfo = activeProvider.GetSandboxInfo(activeSandboxId);
            if (sandboxInfo is not null)
            {
                if (sandboxInfo.RestrictFileSystem)
                {
                    envVars["JCC_SANDBOX_ROOT"] = sandboxInfo.RootPath;
                }
                if (sandboxInfo.RestrictNetwork)
                {
                    envVars["JCC_SANDBOX_NO_NETWORK"] = "1";
                }
                if (sandboxInfo.AllowedPaths is not null)
                {
                    envVars["JCC_SANDBOX_ALLOWED_PATHS"] = string.Join(Path.PathSeparator, sandboxInfo.AllowedPaths);
                }
            }
        }

        var request = new SandboxExecuteRequest
        {
            Command = command,
            WorkingDirectory = workingDir,
            TimeoutMs = 0,
            EnvironmentVariables = envVars.Count > 0 ? envVars : []
        };

        var ipcTask = _ipcClient!.ExecuteAsync(request, ct);

        try
        {
            var completedTask = await Task.WhenAny(ipcTask, Task.Delay(configuredTimeout, ct)).ConfigureAwait(false);

            if (completedTask == ipcTask)
            {
                var response = await ipcTask.ConfigureAwait(false);
                stopwatch.Stop();

                return new AbstractionsSandboxExecutionResult
                {
                    State = response.Success ? SandboxExecutionState.Completed : SandboxExecutionState.Failed,
                    ExecutionId = executionId,
                    Stdout = response.StandardOutput,
                    Stderr = response.StandardError,
                    ExitCode = response.ExitCode,
                    Elapsed = stopwatch.Elapsed,
                    ConfiguredTimeout = configuredTimeout
                };
            }

            if (ct.IsCancellationRequested)
            {
                return new AbstractionsSandboxExecutionResult
                {
                    State = SandboxExecutionState.ForceStopped,
                    ExecutionId = executionId,
                    Elapsed = stopwatch.Elapsed,
                    ConfiguredTimeout = configuredTimeout,
                    ErrorMessage = "外部取消请求，执行已终止"
                };
            }

            _logger?.LogWarning("[SandboxManager] IPC执行超时 - ExecutionId: {Id}, 超时: {Timeout}s, 命令仍在卫星进程中, 不中断", executionId, timeoutSeconds);

            return new AbstractionsSandboxExecutionResult
            {
                State = SandboxExecutionState.TimedOut,
                ExecutionId = executionId,
                Elapsed = stopwatch.Elapsed,
                ConfiguredTimeout = configuredTimeout
            };
        }
        catch (OperationCanceledException)
        {
            return new AbstractionsSandboxExecutionResult
            {
                State = SandboxExecutionState.ForceStopped,
                ExecutionId = executionId,
                Elapsed = stopwatch.Elapsed,
                ConfiguredTimeout = configuredTimeout,
                ErrorMessage = "外部取消请求，执行已终止"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SandboxManager] IPC执行异常，回退到直接执行");
            return await ExecuteDirectlyAsync(command, options, ct).ConfigureAwait(false);
        }
    }

    private async Task<AbstractionsSandboxExecutionResult> ExecuteDirectlyAsync(string command, SandboxExecutionOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);

        var executionId = Guid.NewGuid().ToString("N")[..16];
        var timeoutSeconds = options.GetTimeoutSeconds();
        var configuredTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        var stopwatch = Stopwatch.StartNew();

        var activeProvider = _lifecycleActor.ActiveProvider;
        var activeSandboxId = _lifecycleActor.ActiveSandboxId;

        if (activeProvider is not null && activeSandboxId is not null)
        {
            var sandboxInfo = activeProvider.GetSandboxInfo(activeSandboxId);
            if (sandboxInfo is not null && sandboxInfo.RestrictNetwork
                && !activeProvider.Capabilities.HasFlag(SandboxCapabilities.NetworkIsolation))
            {
                _logger?.LogWarning("[SandboxManager] 网络隔离已请求但当前沙箱类型 {Type} 不支持内核级网络隔离，仅通过环境变量建议性限制", activeProvider.SandboxType.ToValue());
            }

            var providerResult = await activeProvider.ExecuteAsync(
                activeSandboxId, command, null, (int)configuredTimeout.TotalMilliseconds, ct).ConfigureAwait(false);

            if (providerResult is not null)
            {
                var r = providerResult;
                stopwatch.Stop();
                return new AbstractionsSandboxExecutionResult
                {
                    State = r.Success ? SandboxExecutionState.Completed
                        : r.TimedOut ? SandboxExecutionState.TimedOut
                        : SandboxExecutionState.Failed,
                    ExecutionId = executionId,
                    Stdout = r.StandardOutput,
                    Stderr = r.StandardError,
                    ExitCode = r.ExitCode,
                    Elapsed = stopwatch.Elapsed,
                    ConfiguredTimeout = configuredTimeout
                };
            }
        }

        var workingDir = activeProvider is not null && activeSandboxId is not null
            ? activeProvider.ResolvePath(".", activeSandboxId)
            : _fs.GetCurrentDirectory();

        var builder = new IO.ProcessService.ProcessStartInfoBuilder(new IO.ProcessService.ProcessEncodingProvider());
        var processStartInfo = builder.Build(new ProcessOptions
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            ArgumentList = [OperatingSystem.IsWindows() ? "/c" : "-c", command],
            WorkingDirectory = workingDir,
            SkipArgumentValidation = true,
        });

        Process process;
        try
        {
            process = new Process { StartInfo = processStartInfo };
            process.Start();
        }
        catch (Exception ex) when (ex.Message.Contains("目录名称无效") || ex.Message.Contains("directory"))
        {
            _logger?.LogWarning("[SandboxManager] 工作目录无效 '{Dir}'，回退到临时目录", workingDir);
            processStartInfo.WorkingDirectory = Path.GetFullPath(Path.GetTempPath());
            try
            {
                process = new Process { StartInfo = processStartInfo };
                process.Start();
            }
            catch (Exception ex2)
            {
                return new AbstractionsSandboxExecutionResult
                {
                    State = SandboxExecutionState.Failed,
                    ExecutionId = executionId,
                    Elapsed = stopwatch.Elapsed,
                    ConfiguredTimeout = configuredTimeout,
                    ErrorMessage = $"启动进程失败: {ex2.Message}"
                };
            }
        }
        catch (Exception ex)
        {
            return new AbstractionsSandboxExecutionResult
            {
                State = SandboxExecutionState.Failed,
                ExecutionId = executionId,
                Elapsed = stopwatch.Elapsed,
                ConfiguredTimeout = configuredTimeout,
                ErrorMessage = $"启动进程失败: {ex.Message}"
            };
        }

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        var stdoutTask = Task.Run(() =>
        {
            string? line;
            while ((line = process.StandardOutput.ReadLine()) is not null)
            {
                stdoutBuilder.AppendLine(line);
            }
        }, ct);

        var stderrTask = Task.Run(() =>
        {
            string? line;
            while ((line = process.StandardError.ReadLine()) is not null)
            {
                stderrBuilder.AppendLine(line);
            }
        }, ct);

        var execution = new SandboxActiveExecution
        {
            ExecutionId = executionId,
            Process = process,
            StdoutBuilder = stdoutBuilder,
            StderrBuilder = stderrBuilder,
            Stopwatch = stopwatch,
            ConfiguredTimeout = configuredTimeout,
            OriginalCommand = command
        };
        _activeExecutions[executionId] = execution;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(configuredTimeout);

        var processTask = Task.Run(() => process.WaitForExit(), CancellationToken.None);

        try
        {
            var completedTask = await Task.WhenAny(processTask, Task.Delay(configuredTimeout, ct)).ConfigureAwait(false);

            if (completedTask == processTask)
            {
                await processTask.ConfigureAwait(false);

                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

                stopwatch.Stop();
                _activeExecutions.TryRemove(executionId, out _);

                return new AbstractionsSandboxExecutionResult
                {
                    State = process.HasExited && process.ExitCode == 0
                        ? SandboxExecutionState.Completed
                        : SandboxExecutionState.Failed,
                    ExecutionId = executionId,
                    Stdout = stdoutBuilder.ToString(),
                    Stderr = stderrBuilder.ToString(),
                    ExitCode = process.HasExited ? process.ExitCode : null,
                    Elapsed = stopwatch.Elapsed,
                    ConfiguredTimeout = configuredTimeout
                };
            }

            if (ct.IsCancellationRequested)
            {
                ForceStopExecution(executionId);
                return new AbstractionsSandboxExecutionResult
                {
                    State = SandboxExecutionState.ForceStopped,
                    ExecutionId = executionId,
                    Stdout = stdoutBuilder.ToString(),
                    Stderr = stderrBuilder.ToString(),
                    Elapsed = stopwatch.Elapsed,
                    ConfiguredTimeout = configuredTimeout,
                    ErrorMessage = "外部取消请求，执行已终止"
                };
            }

            _logger?.LogWarning("[SandboxManager] 执行超时 - ExecutionId: {Id}, 超时: {Timeout}s, 命令仍在运行, 不中断", executionId, timeoutSeconds);

            return new AbstractionsSandboxExecutionResult
            {
                State = SandboxExecutionState.TimedOut,
                ExecutionId = executionId,
                Stdout = stdoutBuilder.ToString(),
                Stderr = stderrBuilder.ToString(),
                Elapsed = stopwatch.Elapsed,
                ConfiguredTimeout = configuredTimeout
            };
        }
        catch (OperationCanceledException)
        {
            ForceStopExecution(executionId);
            return new AbstractionsSandboxExecutionResult
            {
                State = SandboxExecutionState.ForceStopped,
                ExecutionId = executionId,
                Stdout = stdoutBuilder.ToString(),
                Stderr = stderrBuilder.ToString(),
                Elapsed = stopwatch.Elapsed,
                ConfiguredTimeout = configuredTimeout,
                ErrorMessage = "外部取消请求，执行已终止"
            };
        }
    }

    public async Task<AbstractionsSandboxExecutionResult> ContinueExecutionAsync(string executionId, string action, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(executionId);
        ArgumentException.ThrowIfNullOrEmpty(action);

        if (!_activeExecutions.TryGetValue(executionId, out var execution))
        {
            return new AbstractionsSandboxExecutionResult
            {
                State = SandboxExecutionState.Failed,
                ExecutionId = executionId,
                Elapsed = TimeSpan.Zero,
                ErrorMessage = $"执行 ID '{executionId}' 不存在或已完成"
            };
        }

        if (action.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("[SandboxManager] LLM 决定强行停止执行 - ExecutionId: {Id}", executionId);
            ForceStopExecution(executionId);

            return new AbstractionsSandboxExecutionResult
            {
                State = SandboxExecutionState.ForceStopped,
                ExecutionId = executionId,
                Stdout = execution.StdoutBuilder.ToString(),
                Stderr = execution.StderrBuilder.ToString(),
                Elapsed = execution.Stopwatch.Elapsed,
                ConfiguredTimeout = execution.ConfiguredTimeout,
                ErrorMessage = "LLM 决定强行停止执行"
            };
        }

        if (action.Equals("wait", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("[SandboxManager] LLM 决定继续等待 - ExecutionId: {Id}", executionId);

            var additionalTimeout = execution.ConfiguredTimeout;
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waitCts.CancelAfter(additionalTimeout);

            try
            {
                await Task.Run(() => execution.Process.WaitForExit(), waitCts.Token).ConfigureAwait(false);

                execution.Stopwatch.Stop();
                _activeExecutions.TryRemove(executionId, out _);

                return new AbstractionsSandboxExecutionResult
                {
                    State = execution.Process.HasExited && execution.Process.ExitCode == 0
                        ? SandboxExecutionState.Completed
                        : SandboxExecutionState.Failed,
                    ExecutionId = executionId,
                    Stdout = execution.StdoutBuilder.ToString(),
                    Stderr = execution.StderrBuilder.ToString(),
                    ExitCode = execution.Process.HasExited ? execution.Process.ExitCode : null,
                    Elapsed = execution.Stopwatch.Elapsed,
                    ConfiguredTimeout = execution.ConfiguredTimeout
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger?.LogWarning("[SandboxManager] 继续等待再次超时 - ExecutionId: {Id}", executionId);

                return new AbstractionsSandboxExecutionResult
                {
                    State = SandboxExecutionState.TimedOut,
                    ExecutionId = executionId,
                    Stdout = execution.StdoutBuilder.ToString(),
                    Stderr = execution.StderrBuilder.ToString(),
                    Elapsed = execution.Stopwatch.Elapsed,
                    ConfiguredTimeout = execution.ConfiguredTimeout
                };
            }
        }

        return new AbstractionsSandboxExecutionResult
        {
            State = SandboxExecutionState.Failed,
            ExecutionId = executionId,
            Elapsed = TimeSpan.Zero,
            ErrorMessage = $"未知操作: '{action}'。可用操作: wait (继续等待), stop (强行停止)"
        };
    }

    private void ForceStopExecution(string executionId)
    {
        if (!_activeExecutions.TryRemove(executionId, out var execution))
        {
            return;
        }

        try
        {
            if (!execution.Process.HasExited)
            {
                execution.Process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SandboxManager] 强行停止进程失败 - ExecutionId: {Id}", executionId);
        }

        execution.Stopwatch.Stop();
    }

    protected override void OnDispose()
    {
        _ = _lifecycleActor.DisposeAsync();
    }
}

internal sealed class SandboxActiveExecution
{
    public required string ExecutionId { get; init; }
    public required Process Process { get; init; }
    public required StringBuilder StdoutBuilder { get; init; }
    public required StringBuilder StderrBuilder { get; init; }
    public required Stopwatch Stopwatch { get; init; }
    public required TimeSpan ConfiguredTimeout { get; init; }
    public required string OriginalCommand { get; init; }
}
