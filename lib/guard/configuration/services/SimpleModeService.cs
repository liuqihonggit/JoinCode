namespace Core.Configuration;

/// <summary>
/// 精简模式服务实现 - 管理精简模式的启用/禁用状态和配置
/// </summary>
[Register(typeof(ISimpleModeService), ServiceLifetime.Singleton)]
public sealed partial class SimpleModeService : ServiceEntity, ISimpleModeService {
    private SimpleModeState _state = new(false, SimpleModeConfig.Default);
    private readonly IBriefModeService? _briefModeService;
    private readonly ILogger<SimpleModeService>? _logger;

    private sealed record SimpleModeState(bool IsSimpleMode, SimpleModeConfig Config);

    /// <summary>是否已启用精简模式</summary>
    public bool IsSimpleMode => Volatile.Read(ref _state).IsSimpleMode;

    /// <summary>精简模式状态变更事件 — 启用/禁用/配置更新时触发</summary>
    public event EventHandler<SimpleModeChangedEventArgs>? SimpleModeChanged;

    /// <summary>
    /// 构造函数 — 注入可选的简要模式服务和日志器
    /// </summary>
    public SimpleModeService(
        IBriefModeService? briefModeService = null,
        ILogger<SimpleModeService>? logger = null) {
        _briefModeService = briefModeService;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Enable() {
        SimpleModeState old;
        while (true) {
            old = Volatile.Read(ref _state);
            if (old.IsSimpleMode) return;
            if (Interlocked.CompareExchange(ref _state, old with { IsSimpleMode = true }, old) == old) break;
        }

        _logger?.LogInformation("Simple Mode enabled");
        _briefModeService?.Enable();

        SimpleModeChanged?.Invoke(this, new SimpleModeChangedEventArgs {
            IsSimpleMode = true,
            Config = old.Config
        });
    }

    /// <inheritdoc />
    public void Disable() {
        SimpleModeState old;
        while (true) {
            old = Volatile.Read(ref _state);
            if (!old.IsSimpleMode) return;
            if (Interlocked.CompareExchange(ref _state, old with { IsSimpleMode = false }, old) == old) break;
        }

        _logger?.LogInformation("Simple Mode disabled");
        _briefModeService?.Disable();

        SimpleModeChanged?.Invoke(this, new SimpleModeChangedEventArgs {
            IsSimpleMode = false,
            Config = old.Config
        });
    }

    /// <inheritdoc />
    public bool Toggle() {
        SimpleModeState old;
        bool newState;
        while (true) {
            old = Volatile.Read(ref _state);
            newState = !old.IsSimpleMode;
            if (Interlocked.CompareExchange(ref _state, old with { IsSimpleMode = newState }, old) == old) break;
        }

        _logger?.LogInformation(newState ? "Simple Mode enabled" : "Simple Mode disabled");

        if (newState)
            _briefModeService?.Enable();
        else
            _briefModeService?.Disable();

        SimpleModeChanged?.Invoke(this, new SimpleModeChangedEventArgs {
            IsSimpleMode = newState,
            Config = old.Config
        });

        return newState;
    }

    /// <inheritdoc />
    public SimpleModeConfig GetCurrentConfig() => Volatile.Read(ref _state).Config;

    /// <inheritdoc />
    public void UpdateConfig(SimpleModeConfig config) {
        ArgumentNullException.ThrowIfNull(config);

        SimpleModeState old;
        while (true) {
            old = Volatile.Read(ref _state);
            if (Interlocked.CompareExchange(ref _state, old with { Config = config }, old) == old) break;
        }

        _logger?.LogDebug("Simple Mode config updated");

        SimpleModeChanged?.Invoke(this, new SimpleModeChangedEventArgs {
            IsSimpleMode = old.IsSimpleMode,
            Config = config
        });
    }

    /// <inheritdoc />
    public override void Dispose() {
        base.Dispose();
    }
}