namespace JoinCode.Hands.Desktop;

/// <summary>
/// 窗口震动去抖协调器 — 进程内单例，1 秒去抖合并多个子代理的震动请求。
/// 用 <c>Interlocked.CompareExchange(ref int, int, int)</c> 无锁实现，避免锁竞争。
/// <para>配置联动：注入 <see cref="IConfigurationService"/> 读取 <c>windowShakeEnabled</c> 配置项，</para>
/// <para>启动时后台读取初始值，监听 <see cref="IConfigurationService.SettingChanged"/> 事件热更新。</para>
/// </summary>
[Register(typeof(IWindowShakeCoordinator), ServiceLifetime.Singleton)]
public sealed class WindowShakeCoordinator : ServiceEntity, IWindowShakeCoordinator
{
    /// <summary>
    /// 上次震动的 <see cref="Environment.TickCount"/> — volatile 保证可见性，Interlocked 保证原子性。
    /// </summary>
    private volatile int _lastShakeTick;

    /// <summary>
    /// 震动最小间隔（毫秒） — 1 秒去抖。
    /// </summary>
    private const int ShakeIntervalMs = 1000;

    /// <summary>
    /// 配置提供回调 — 返回 WindowShakeEnabled 配置值，null 表示未配置（默认启用）。
    /// </summary>
    private readonly Func<bool?>? _shakeEnabledProvider;

    /// <summary>
    /// 配置服务引用 — 用于取消事件订阅。
    /// </summary>
    private readonly IConfigurationService? _configService;

    /// <summary>
    /// 从配置缓存的启用状态 — 默认 true，后台任务读取后更新。
    /// </summary>
    private volatile bool _cachedEnabled = true;

    /// <summary>
    /// 构造窗口震动去抖协调器。
    /// </summary>
    /// <param name="configService">配置服务（可选，读取 windowShakeEnabled 配置项）。</param>
    /// <param name="shakeEnabledProvider">震动开关配置提供回调（可选，优先于配置服务）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public WindowShakeCoordinator(
        IConfigurationService? configService = null,
        Func<bool?>? shakeEnabledProvider = null,
        ILogger<WindowShakeCoordinator>? logger = null)
    {
        _shakeEnabledProvider = shakeEnabledProvider;
        _configService = configService;

        if (configService is not null)
        {
            _ = ReadConfigAsync(configService, logger);
            configService.SettingChanged += OnSettingChanged;
        }
    }

    /// <summary>
    /// 震动功能是否启用 — 优先用回调，否则用配置缓存值。
    /// </summary>
    public bool IsShakeEnabled => _shakeEnabledProvider?.Invoke() ?? _cachedEnabled;

    /// <summary>
    /// 尝试获取震动时间槽。1 秒内只允许一次成功。
    /// </summary>
    /// <returns>true 表示可执行震动；false 表示 1 秒内已震动过。</returns>
    public bool TryAcquireShakeSlot()
    {
        var now = Environment.TickCount;
        var last = _lastShakeTick;
        if (now - last < ShakeIntervalMs)
            return false;
        return Interlocked.CompareExchange(ref _lastShakeTick, now, last) == last;
    }

    /// <summary>
    /// 后台读取 windowShakeEnabled 配置初始值。
    /// </summary>
    private async Task ReadConfigAsync(IConfigurationService configService, ILogger<WindowShakeCoordinator>? logger)
    {
        try
        {
            var value = await configService.GetAsync("windowShakeEnabled", CancellationToken.None).ConfigureAwait(false);
            if (bool.TryParse(value, out var enabled))
                _cachedEnabled = enabled;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "WindowShakeCoordinator: failed to read windowShakeEnabled config");
        }
    }

    /// <summary>
    /// 配置变更事件处理 — 更新缓存值。
    /// </summary>
    private void OnSettingChanged(object? sender, SettingChangeEventArgs e)
    {
        if (e.Key == "windowShakeEnabled" && bool.TryParse(e.NewValue, out var enabled))
            _cachedEnabled = enabled;
    }

    /// <summary>
    /// 释放 — 取消配置变更事件订阅。
    /// </summary>
    public override void Dispose()
    {
        if (_configService is not null)
            _configService.SettingChanged -= OnSettingChanged;
        base.Dispose();
    }
}
