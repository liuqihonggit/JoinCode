namespace Infrastructure.EntityReaper;

/// <summary>
/// 后台实体回收调度服务 — 定期调用 EntityReaper.ScanOnce()
/// 启动后延迟 30 秒执行首次扫描，之后按 ScanInterval 循环扫描
/// </summary>
public sealed class BackgroundEntityReaperService : PeriodicBackgroundServiceBase {
    private readonly IEntityReaper _reaper;
    private readonly IClockService _clock;
    private readonly EntityReaperConfig _config;
    private readonly ILogger<BackgroundEntityReaperService>? _logger;

    /// <inheritdoc/>
    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(30);
    /// <inheritdoc/>
    protected override TimeSpan Interval => _config.ScanInterval;
    /// <inheritdoc/>
    protected override IClockService Clock => _clock;
    /// <inheritdoc/>
    protected override ILogger? Logger => _logger;
    /// <inheritdoc/>
    protected override string ServiceName => "后台实体回收服务";

    /// <summary>
    /// 构造函数 — 注入实体回收器、时钟服务、配置和日志
    /// </summary>
    /// <param name="reaper">实体回收器</param>
    /// <param name="clock">时钟服务</param>
    /// <param name="config">回收配置，为 null 时使用默认配置</param>
    /// <param name="logger">日志记录器，可为 null</param>
    public BackgroundEntityReaperService(
        IEntityReaper reaper,
        IClockService clock,
        EntityReaperConfig? config = null,
        ILogger<BackgroundEntityReaperService>? logger = null) {
        _reaper = reaper;
        _clock = clock;
        _config = config ?? new EntityReaperConfig();
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken cancellationToken) {
        _reaper.ScanOnce();
        return Task.CompletedTask;
    }
}