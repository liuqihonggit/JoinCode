namespace IO.Services;

/// <summary>
/// 主动状态服务 — 管理主动模式激活/暂停/上下文阻塞等运行时状态，并通过事件通知订阅者
/// </summary>
[Register(typeof(IProactiveStateService), ServiceLifetime.Singleton)]
public sealed partial class ProactiveStateService : ServiceEntity, IProactiveStateService {
    private bool _active;
    private bool _paused;
    private bool _contextBlocked;
    private readonly ILogger<ProactiveStateService>? _logger;
    private event EventHandler? _stateChanged;

    /// <summary>
    /// 构造函数 — 注入可选的日志记录器
    /// </summary>
    /// <param name="logger">日志记录器实例，为 null 时不记录日志</param>
    public ProactiveStateService(ILogger<ProactiveStateService>? logger = null) {
        _logger = logger;
    }

    /// <summary>
    /// 获取主动模式是否已激活
    /// </summary>
    public bool IsActive => _active;

    /// <summary>
    /// 获取主动模式是否已暂停
    /// </summary>
    public bool IsPaused => _paused;

    /// <summary>
    /// 获取上下文是否处于阻塞状态
    /// </summary>
    public bool IsContextBlocked => _contextBlocked;

    /// <summary>
    /// 状态变更事件 — 当激活/暂停/阻塞等状态发生变化时触发
    /// </summary>
    public event EventHandler? StateChanged {
        add => _stateChanged += value;
        remove => _stateChanged -= value;
    }

    /// <summary>
    /// 激活主动模式，并清除暂停状态
    /// </summary>
    /// <param name="source">激活来源标识，用于日志记录；为 null 时记为 "unknown"</param>
    public void Activate(string? source = null) {
        _active = true;
        _paused = false;
        _logger?.LogInformation("主动模式已激活 (来源: {Source})", source ?? "unknown");
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 停用主动模式，并清除暂停状态
    /// </summary>
    public void Deactivate() {
        _active = false;
        _paused = false;
        _logger?.LogInformation("主动模式已停用");
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 暂停主动模式（不改变激活状态）
    /// </summary>
    public void Pause() {
        _paused = true;
        _logger?.LogInformation("主动模式已暂停");
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 恢复主动模式（清除暂停状态）
    /// </summary>
    public void Resume() {
        _paused = false;
        _logger?.LogInformation("主动模式已恢复");
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 设置上下文阻塞状态
    /// </summary>
    /// <param name="blocked">true 表示阻塞上下文；false 表示解除阻塞</param>
    public void SetContextBlocked(bool blocked) {
        _contextBlocked = blocked;
        _logger?.LogDebug("上下文阻塞: {Blocked}", blocked);
        _stateChanged?.Invoke(this, EventArgs.Empty);
    }
}