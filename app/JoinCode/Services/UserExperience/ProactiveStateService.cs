using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class ProactiveStateService : IProactiveStateService
{
    private bool _active;
    private bool _paused;
    private bool _contextBlocked;
    [Inject] private readonly ILogger<ProactiveStateService>? _logger;
    private event EventHandler? _stateChanged;

    public ProactiveStateService(ILogger<ProactiveStateService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsActive => _active;
    public bool IsPaused => _paused;
    public bool IsContextBlocked => _contextBlocked;

    public event EventHandler? StateChanged
    {
        add => _stateChanged += value;
        remove => _stateChanged -= value;
    }

    public void Activate(string? source = null)
    {
        _active = true;
        _paused = false;
        _logger?.LogInformation("主动模式已激活 (来源: {Source})", source ?? "unknown");
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate()
    {
        _active = false;
        _paused = false;
        _logger?.LogInformation("主动模式已停用");
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        _paused = true;
        _logger?.LogInformation("主动模式已暂停");
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        _paused = false;
        _logger?.LogInformation("主动模式已恢复");
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetContextBlocked(bool blocked)
    {
        _contextBlocked = blocked;
        _logger?.LogDebug("上下文阻塞: {Blocked}", blocked);
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }
}
