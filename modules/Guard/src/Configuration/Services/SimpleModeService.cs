
using JoinCode.Abstractions.Attributes;

namespace Core.Configuration;

/// <summary>
/// 精简模式服务实现 - 管理精简模式的启用/禁用状态和配置
/// </summary>
[Register]
public sealed partial class SimpleModeService : ISimpleModeService
{
    private readonly object _lock = new();
    private bool _isSimpleMode;
    private SimpleModeConfig _config;
    private readonly IBriefModeService? _briefModeService;
    [Inject] private readonly ILogger<SimpleModeService>? _logger;

    public bool IsSimpleMode
    {
        get { lock (_lock) return _isSimpleMode; }
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
        lock (_lock)
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
        lock (_lock)
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
        lock (_lock)
        {
            if (_isSimpleMode)
            {
                Disable();
            }
            else
            {
                Enable();
            }

            return _isSimpleMode;
        }
    }

    public SimpleModeConfig GetCurrentConfig()
    {
        lock (_lock) return _config;
    }

    public void UpdateConfig(SimpleModeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_lock)
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
