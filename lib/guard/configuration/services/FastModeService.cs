namespace Core.Configuration;

/// <summary>
/// 快速模式服务 — 在主模型与快速模型间切换,带冷却计时器自动回退
/// </summary>
[Register(typeof(IFastModeService), ServiceLifetime.Singleton)]
public sealed partial class FastModeService : ServiceEntity, IFastModeService, IDisposable {
    private readonly AsyncLock _lock = new("FastModeService");
    private bool _isActive;
    private string _fastModelId;
    private string _primaryModelId;
    private Timer? _cooldownTimer;
    private readonly TimeSpan _cooldownDuration;
    private readonly ILogger<FastModeService>? _logger;
    private bool _disposed;

    /// <summary>快速模式是否当前激活</summary>
    public bool IsFastModeActive {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) return _isActive; }
    }

    /// <summary>快速模型标识</summary>
    public string FastModelId {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) return _fastModelId; }
    }

    /// <summary>主模型标识</summary>
    public string PrimaryModelId {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) return _primaryModelId; }
    }

    /// <summary>快速模式变更事件 — 激活/停用时触发</summary>
    public event EventHandler<FastModeChangedEventArgs>? FastModeChanged;

    /// <summary>
    /// 初始化快速模式服务
    /// </summary>
    /// <param name="config">可选的工作流配置(取主模型标识)</param>
    /// <param name="fastModelId">可选的快速模型标识</param>
    /// <param name="cooldownDuration">可选的冷却时长(默认 5 分钟)</param>
    /// <param name="logger">可选的日志记录器</param>
    /// <param name="modelConfigLoader">可选的模型配置加载器</param>
    public FastModeService(
        WorkflowConfig? config = null,
        string? fastModelId = null,
        TimeSpan? cooldownDuration = null,
        ILogger<FastModeService>? logger = null,
        IModelConfigLoader? modelConfigLoader = null) {
        var loader = modelConfigLoader ?? new ModelConfigLoader();
        _primaryModelId = config?.Provider?.ModelId ?? loader.GetDefaultModelId(VendorKindEnumConstants.OpenAi);
        _fastModelId = fastModelId ?? loader.GetDefaultFastModelId(VendorKindEnumConstants.OpenAi);
        _cooldownDuration = cooldownDuration ?? TimeSpan.FromMinutes(5);
        _logger = logger;
    }

    /// <summary>激活快速模式 — 切换到快速模型并启动冷却计时器</summary>
    public void Activate() {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            if (_isActive) return;

            _isActive = true;
            _logger?.LogInformation("Fast Mode activated: {FastModel}", _fastModelId);
        }

        StartCooldownTimer();
        FastModeChanged?.Invoke(this, new FastModeChangedEventArgs {
            IsFastModeActive = true,
            ActiveModelId = _fastModelId,
            InactiveModelId = _primaryModelId
        });
    }

    /// <summary>停用快速模式 — 切换回主模型并停止冷却计时器</summary>
    public void Deactivate() {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            if (!_isActive) return;

            _isActive = false;
            StopCooldownTimerUnchecked();
            _logger?.LogInformation("Fast Mode deactivated: returning to {PrimaryModel}", _primaryModelId);
        }

        FastModeChanged?.Invoke(this, new FastModeChangedEventArgs {
            IsFastModeActive = false,
            ActiveModelId = _primaryModelId,
            InactiveModelId = _fastModelId
        });
    }

    /// <summary>切换快速模式开关 — 激活时停用,停用时激活</summary>
    public void Toggle() {
        bool shouldActivate;
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            shouldActivate = !_isActive;
        }

        // 锁外调用（避免锁内调用 Activate/Deactivate 导致重入死锁）
        if (shouldActivate)
            Activate();
        else
            Deactivate();
    }

    /// <summary>
    /// 设置快速模型标识
    /// </summary>
    /// <param name="modelId">模型标识</param>
    public void SetFastModel(string modelId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            _fastModelId = modelId;
        }
        _logger?.LogDebug("Fast model set to: {ModelId}", modelId);
    }

    /// <summary>
    /// 设置主模型标识
    /// </summary>
    /// <param name="modelId">模型标识</param>
    public void SetPrimaryModel(string modelId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            _primaryModelId = modelId;
        }
        _logger?.LogDebug("Primary model set to: {ModelId}", modelId);
    }

    /// <summary>
    /// 获取当前生效的模型标识 — 快速模式激活时返回快速模型,否则返回主模型
    /// </summary>
    /// <returns>当前模型标识</returns>
    public string GetCurrentModelId() {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            return _isActive ? _fastModelId : _primaryModelId;
        }
    }

    /// <summary>是否处于冷却期 — 快速模式激活且冷却计时器仍在运行</summary>
    public bool IsInCooldown() {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            return _isActive && _cooldownTimer != null;
        }
    }

    private void StartCooldownTimer() {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            StopCooldownTimerUnchecked();
            _cooldownTimer = new Timer(_ => {
                _logger?.LogDebug("Fast Mode cooldown expired, auto-deactivating");
                Deactivate();
            }, null, _cooldownDuration, Timeout.InfiniteTimeSpan);
        }
    }

    private void StopCooldownTimerUnchecked() {
        _cooldownTimer?.Dispose();
        _cooldownTimer = null;
    }

    /// <inheritdoc />
    public override void Dispose() {
        if (_disposed) return;
        _disposed = true;

        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) {
            StopCooldownTimerUnchecked();
        }
        base.Dispose();
    }
}