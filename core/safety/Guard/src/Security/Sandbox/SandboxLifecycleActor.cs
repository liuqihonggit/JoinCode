namespace Core.Security.Sandbox;

/// <summary>
/// 沙箱生命周期 Actor 命令 — Channel 中的消息类型
/// </summary>
internal interface ISandboxCommand;

internal sealed record EnterSandboxCmd(SandboxOptions Options, CancellationToken Ct, TaskCompletionSource<SandboxInfo> Tcs) : ISandboxCommand;
internal sealed record ExitSandboxCmd(CancellationToken Ct, TaskCompletionSource Tcs) : ISandboxCommand;
internal sealed record SwitchProviderCmd(SandboxType Type, CancellationToken Ct, TaskCompletionSource Tcs) : ISandboxCommand;

/// <summary>
/// 沙箱生命周期 Actor — 独占 _activeProvider/_activeSandboxId/_healthState，
/// Consumer 串行处理 Enter/Exit/Switch 命令，消除 AsyncLock 锁内长 await（容器创建/销毁 >5s）。
/// 状态由 Consumer 线程独占写入，外部通过 volatile 读取。
/// </summary>
internal sealed class SandboxLifecycleActor : ActorBase<ISandboxCommand, Unit>
{
    private readonly ConcurrentDictionary<SandboxType, ISandboxProvider> _providers;
    private readonly ILogger<SandboxManager>? _logger;

    private volatile ISandboxProvider? _activeProvider;
    private volatile string? _activeSandboxId;
    private volatile SandboxHealthState _healthState = SandboxHealthState.Healthy;

    public SandboxLifecycleActor(
        ConcurrentDictionary<SandboxType, ISandboxProvider> providers,
        ILogger<SandboxManager>? logger)
        : base()
    {
        _providers = providers;
        _logger = logger;
    }

    public ISandboxProvider? ActiveProvider => _activeProvider;
    public string? ActiveSandboxId => _activeSandboxId;
    public SandboxHealthState HealthState => _healthState;

    public bool IsInSandbox => _activeProvider is not null && _activeSandboxId is not null && _activeProvider.GetSandboxInfo(_activeSandboxId) is not null;
    public SandboxInfo? CurrentSandbox => _activeSandboxId is not null ? _activeProvider?.GetSandboxInfo(_activeSandboxId) : null;

    /// <summary>
    /// 外部设置健康状态 — 仅在生命周期命令完成后调用，无并发写入风险。
    /// </summary>
    public void SetHealthState(SandboxHealthState state) => _healthState = state;

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 发送 Enter 命令并等待完成
    /// </summary>
    public async Task<SandboxInfo> EnterAsync(SandboxOptions options, CancellationToken ct)
    {
        var tcs = CreateTcs<SandboxInfo>();
        await SendAsync(new EnterSandboxCmd(options, ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 发送 Exit 命令并等待完成
    /// </summary>
    public async Task ExitAsync(CancellationToken ct)
    {
        var tcs = CreateTcs();
        await SendAsync(new ExitSandboxCmd(ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 发送 Switch 命令并等待完成
    /// </summary>
    public async Task SwitchAsync(SandboxType type, CancellationToken ct)
    {
        var tcs = CreateTcs();
        await SendAsync(new SwitchProviderCmd(type, ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    protected override async ValueTask HandleAsync(ISandboxCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case EnterSandboxCmd cmd:
            {
                if (IsInSandbox)
                {
                    cmd.Tcs.TrySetException(new InvalidOperationException($"[GRD008] 已在 {_activeProvider!.SandboxType} 沙箱中，请先退出再进入新沙箱"));
                    break;
                }

                var (provider, fallbackUsed) = ResolveProviderWithFallback(cmd.Options.Type);

                try
                {
                    var info = await provider.CreateSandboxAsync(cmd.Options, cmd.Ct).ConfigureAwait(false);

                    _activeProvider = provider;
                    _activeSandboxId = info.SandboxId;
                    _healthState = SandboxHealthState.Healthy;

                    _logger?.LogInformation("[SandboxManager] 沙箱已激活 - 类型: {Type}, Id: {Id}, 降级: {Fallback}", info.Type, info.SandboxId, fallbackUsed);

                    cmd.Tcs.TrySetResult(info);
                }
                catch (Exception ex) when (fallbackUsed)
                {
                    _healthState = SandboxHealthState.Fallback;
                    cmd.Tcs.TrySetException(ex);
                }
                catch (Exception ex)
                {
                    cmd.Tcs.TrySetException(ex);
                }

                break;
            }

            case ExitSandboxCmd cmd:
            {
                if (_activeProvider is null || _activeSandboxId is null)
                {
                    _logger?.LogDebug("[SandboxManager] 不在沙箱中，无需退出");
                    cmd.Tcs.TrySetResult();
                    break;
                }

                var provider = _activeProvider;
                var sandboxId = _activeSandboxId;

                try
                {
                    await provider.DestroySandboxAsync(sandboxId, cmd.Ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[SandboxManager] 销毁沙箱异常，强制清理 - Id: {Id}", sandboxId);
                    _healthState = SandboxHealthState.Degraded;
                }

                _activeProvider = null;
                _activeSandboxId = null;

                _logger?.LogInformation("[SandboxManager] 沙箱已退出 - 类型: {Type}", provider.SandboxType);
                cmd.Tcs.TrySetResult();
                break;
            }

            case SwitchProviderCmd cmd:
            {
                var previousType = _activeProvider?.SandboxType ?? SandboxType.None;
                var previousInfo = CurrentSandbox;

                if (_activeProvider is not null && _activeSandboxId is not null)
                {
                    try
                    {
                        await _activeProvider.DestroySandboxAsync(_activeSandboxId, cmd.Ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[SandboxManager] 切换时销毁旧沙箱异常 - Id: {Id}", _activeSandboxId);
                    }
                }

                var (newProvider, fallbackUsed) = ResolveProviderWithFallback(cmd.Type);

                var newOptions = new SandboxOptions
                {
                    Type = newProvider.SandboxType,
                    RestrictNetwork = previousInfo?.RestrictNetwork ?? true,
                    RestrictFileSystem = previousInfo?.RestrictFileSystem ?? true,
                    AllowedPaths = previousInfo?.AllowedPaths ?? [],
                    SandboxRoot = previousInfo?.RootPath,
                    MemoryLimitMb = 0,
                    CpuLimitPercent = 0,
                    TimeLimitSeconds = 0
                };

                var newInfo = await newProvider.CreateSandboxAsync(newOptions, cmd.Ct).ConfigureAwait(false);

                _activeProvider = newProvider;
                _activeSandboxId = newInfo.SandboxId;
                _healthState = fallbackUsed ? SandboxHealthState.Fallback : SandboxHealthState.Healthy;

                _logger?.LogInformation("[SandboxManager] 沙箱切换: {From} → {To}, 新 Id: {Id}, 降级: {Fallback}",
                    previousType.ToValue(), newProvider.SandboxType.ToValue(), newInfo.SandboxId, fallbackUsed);

                cmd.Tcs.TrySetResult();
                break;
            }
        }
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

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[SandboxManager] Lifecycle Actor Consumer 异常");
    }
}
