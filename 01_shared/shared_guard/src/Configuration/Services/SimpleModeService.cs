namespace Core.Configuration;

/// <summary>
/// 精简模式服务实现 - 管理精简模式的启用/禁用状态和配置
/// </summary>
[Register(typeof(ISimpleModeService), ServiceLifetime.Singleton)]
public sealed partial class SimpleModeService : ServiceEntity, ISimpleModeService
{
    private readonly AsyncLock _lock = new("SimpleModeService");
    private bool _isSimpleMode;
    private SimpleModeConfig _config;
    private readonly IBriefModeService? _briefModeService;
    private readonly ILogger<SimpleModeService>? _logger;

    public bool IsSimpleMode
    {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) return _isSimpleMode; }
    }

    public event EventHandler<SimpleModeChangedEventArgs>? SimpleModeChanged;

    public SimpleModeService(
        IBriefModeService? briefModeService = null,
        ILogger<SimpleModeService>? logger = null)
    {
        _briefModeService = briefModeService;
        _logger = logger;
        _config = SimpleModeConfig.Default;
    }

    public void Enable()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            if (_isSimpleMode) return;

            _isSimpleMode = true;
            _logger?.LogInformation("Simple Mode enabled");
        }

        // 启用精简模式时同步启用简要模式
        _briefModeService?.Enable();

        SimpleModeChanged?.Invoke(this, new SimpleModeChangedEventArgs
        {
            IsSimpleMode = true,
            Config = _config
        });
    }

    public void Disable()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            if (!_isSimpleMode) return;

            _isSimpleMode = false;
            _logger?.LogInformation("Simple Mode disabled");
        }

        // 禁用精简模式时同步禁用简要模式
        _briefModeService?.Disable();

        SimpleModeChanged?.Invoke(this, new SimpleModeChangedEventArgs
        {
            IsSimpleMode = false,
            Config = _config
        });
    }

    public bool Toggle()
    {
        bool newState;
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            newState = !_isSimpleMode;
            _isSimpleMode = newState;
            _logger?.LogInformation(newState ? "Simple Mode enabled" : "Simple Mode disabled");
        }

        // 锁外处理副作用（避免锁内调用 Enable/Disable 导致重入死锁）
        if (newState)
            _briefModeService?.Enable();
        else
            _briefModeService?.Disable();

        SimpleModeChanged?.Invoke(this, new SimpleModeChangedEventArgs
        {
            IsSimpleMode = newState,
            Config = _config
        });

        return newState;
    }

    public SimpleModeConfig GetCurrentConfig()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) return _config;
    }

    public void UpdateConfig(SimpleModeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            _config = config;
            _logger?.LogDebug("Simple Mode config updated");
        }

        // 配置变更时通知订阅者
        SimpleModeChanged?.Invoke(this, new SimpleModeChangedEventArgs
        {
            IsSimpleMode = _isSimpleMode,
            Config = config
        });
    }
}
