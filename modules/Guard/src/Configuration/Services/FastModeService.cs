using JoinCode.Abstractions.Attributes;

namespace Core.Configuration;

[Register]
public sealed partial class FastModeService : IFastModeService, IDisposable
{
    private readonly object _lock = new();
    private bool _isActive;
    private string _fastModelId;
    private string _primaryModelId;
    private Timer? _cooldownTimer;
    private readonly TimeSpan _cooldownDuration;
    [Inject] private readonly ILogger<FastModeService>? _logger;

    public bool IsFastModeActive
    {
        get { lock (_lock) return _isActive; }
    }

    public string FastModelId
    {
        get { lock (_lock) return _fastModelId; }
    }

    public string PrimaryModelId
    {
        get { lock (_lock) return _primaryModelId; }
    }

    public event EventHandler<FastModeChangedEventArgs>? FastModeChanged;

    public FastModeService(
        WorkflowConfig? config = null,
        string? fastModelId = null,
        TimeSpan? cooldownDuration = null,
        ILogger<FastModeService>? logger = null)
    {
        _primaryModelId = config?.Provider?.ModelId ?? ModelConfigLoader.GetDefaultModelId("openai");
        _fastModelId = fastModelId ?? ModelConfigLoader.GetDefaultFastModelId("openai");
        _cooldownDuration = cooldownDuration ?? TimeSpan.FromMinutes(5);
        _logger = logger;
    }

    public void Activate()
    {
        lock (_lock)
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
        lock (_lock)
        {
            if (!_isActive) return;

            _isActive = false;
            StopCooldownTimer();
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
        lock (_lock)
        {
            if (_isActive)
            {
                Deactivate();
            }
            else
            {
                Activate();
            }
        }
    }

    public void SetFastModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        lock (_lock)
        {
            _fastModelId = modelId;
        }
        _logger?.LogDebug("Fast model set to: {ModelId}", modelId);
    }

    public void SetPrimaryModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        lock (_lock)
        {
            _primaryModelId = modelId;
        }
        _logger?.LogDebug("Primary model set to: {ModelId}", modelId);
    }

    public string GetCurrentModelId()
    {
        lock (_lock)
        {
            return _isActive ? _fastModelId : _primaryModelId;
        }
    }

    public bool IsInCooldown()
    {
        lock (_lock)
        {
            return _isActive && _cooldownTimer != null;
        }
    }

    private void StartCooldownTimer()
    {
        StopCooldownTimer();
        lock (_lock)
        {
            _cooldownTimer = new Timer(_ =>
            {
                _logger?.LogDebug("Fast Mode cooldown expired, auto-deactivating");
                Deactivate();
            }, null, _cooldownDuration, Timeout.InfiniteTimeSpan);
        }
    }

    private void StopCooldownTimer()
    {
        lock (_lock)
        {
            _cooldownTimer?.Dispose();
            _cooldownTimer = null;
        }
    }

    public void Dispose()
    {
        StopCooldownTimer();
    }
}
