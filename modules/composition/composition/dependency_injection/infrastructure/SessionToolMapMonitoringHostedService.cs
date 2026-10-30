namespace Core.DependencyInjection;

/// <summary>
/// SessionToolMap 监控启动托管服务 — 启动后台监控循环（ADR 0152 + ADR 0153）
/// <para>
/// 在应用启动时调 SessionToolMap.StartMonitoring(notifier)，启动后台监控循环。
/// WarningThreshold 到达时通过 IToolTimeoutNotifier 注入 steer 提示唤醒宿主 AI。
/// 应用停止时调 StopMonitoring 停止监控。
/// </para>
/// </summary>
public sealed class SessionToolMapMonitoringHostedService : IHostedService {
    private readonly SessionToolMap _toolMap;
    private readonly IToolTimeoutNotifier? _notifier;
    private readonly ILogger<SessionToolMapMonitoringHostedService>? _logger;

    /// <summary>
    /// 初始化 <see cref="SessionToolMapMonitoringHostedService"/> 实例。
    /// </summary>
    /// <param name="toolMap">session 级别工具生命周期 map</param>
    /// <param name="notifier">工具超时通知器（可选，null=仅杀死不通知）</param>
    /// <param name="logger">日志记录器</param>
    public SessionToolMapMonitoringHostedService(
        SessionToolMap toolMap,
        IToolTimeoutNotifier? notifier = null,
        ILogger<SessionToolMapMonitoringHostedService>? logger = null) {
        _toolMap = toolMap ?? throw new ArgumentNullException(nameof(toolMap));
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// 应用启动时触发 — 启动后台监控循环。
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) {
        _toolMap.StartMonitoring(_notifier, ct: cancellationToken);
        _logger?.LogInformation("[SessionToolMap] 监控托管服务已激活，后台监控循环运行中");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 应用停止时触发 — 停止后台监控。
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) {
        _toolMap.StopMonitoring();
        _logger?.LogInformation("[SessionToolMap] 监控托管服务停止");
        return Task.CompletedTask;
    }
}
