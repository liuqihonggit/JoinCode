namespace Core.Configuration;

[Register(typeof(IFastModeService), ServiceLifetime.Singleton)]
public sealed partial class FastModeService : ServiceEntity, IFastModeService, IDisposable
{
    private readonly AsyncLock _lock = new("FastModeService");
    private bool _isActive;
    private string _fastModelId;
    private string _primaryModelId;
    private Timer? _cooldownTimer;
    private readonly TimeSpan _cooldownDuration;
    private readonly ILogger<FastModeService>? _logger;

    public bool IsFastModeActive
    {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) return _isActive; }
    }

    public string FastModelId
    {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) return _fastModelId; }
    }

    public string PrimaryModelId
    {
        get { using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")) return _primaryModelId; }
    }

    public event EventHandler<FastModeChangedEventArgs>? FastModeChanged;

    public FastModeService(
        WorkflowConfig? config = null,
        string? fastModelId = null,
        TimeSpan? cooldownDuration = null,
        ILogger<FastModeService>? logger = null,
        IModelConfigLoader? modelConfigLoader = null)
    {
        var loader = modelConfigLoader ?? new ModelConfigLoader();
        _primaryModelId = config?.Provider?.ModelId ?? loader.GetDefaultModelId(VendorKindConstants.OpenAi);
        _fastModelId = fastModelId ?? loader.GetDefaultFastModelId(VendorKindConstants.OpenAi);
        _cooldownDuration = cooldownDuration ?? TimeSpan.FromMinutes(5);
        _logger = logger;
    }

    public void Activate()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            if (_isActive) return;

            _isActive = true;
            _logger?.LogInformation("Fast Mode activated: {FastModel}", _fastModelId);
        }

        StartCooldownTimer();
        FastModeChanged?.Invoke(this, new FastModeChangedEventArgs
        {
            IsFastModeActive = true,
            ActiveModelId = _fastModelId,
            InactiveModelId = _primaryModelId
        });
    }

    public void Deactivate()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            if (!_isActive) return;

            _isActive = false;
            StopCooldownTimerUnchecked();
            _logger?.LogInformation("Fast Mode deactivated: returning to {PrimaryModel}", _primaryModelId);
        }

        FastModeChanged?.Invoke(this, new FastModeChangedEventArgs
        {
            IsFastModeActive = false,
            ActiveModelId = _primaryModelId,
            InactiveModelId = _fastModelId
        });
    }

    public void Toggle()
    {
        bool shouldActivate;
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            shouldActivate = !_isActive;
        }

        // 锁外调用（避免锁内调用 Activate/Deactivate 导致重入死锁）
        if (shouldActivate)
            Activate();
        else
            Deactivate();
    }

    public void SetFastModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            _fastModelId = modelId;
        }
        _logger?.LogDebug("Fast model set to: {ModelId}", modelId);
    }

    public void SetPrimaryModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            _primaryModelId = modelId;
        }
        _logger?.LogDebug("Primary model set to: {ModelId}", modelId);
    }

    public string GetCurrentModelId()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            return _isActive ? _fastModelId : _primaryModelId;
        }
    }

    public bool IsInCooldown()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            return _isActive && _cooldownTimer != null;
        }
    }

    private void StartCooldownTimer()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            StopCooldownTimerUnchecked();
            _cooldownTimer = new Timer(_ =>
            {
                _logger?.LogDebug("Fast Mode cooldown expired, auto-deactivating");
                Deactivate();
            }, null, _cooldownDuration, Timeout.InfiniteTimeSpan);
        }
    }

    private void StopCooldownTimerUnchecked()
    {
        _cooldownTimer?.Dispose();
        _cooldownTimer = null;
    }

    protected override void OnDispose()
    {
        using (_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时"))
        {
            StopCooldownTimerUnchecked();
        }
    }
}
