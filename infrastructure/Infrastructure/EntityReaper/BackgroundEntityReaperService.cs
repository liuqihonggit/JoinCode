namespace Infrastructure.EntityReaper;

/// <summary>
/// 后台实体回收调度服务 — 定期调用 EntityReaper.ScanOnce()
/// 启动后延迟 30 秒执行首次扫描，之后按 ScanInterval 循环扫描
/// </summary>
public sealed class BackgroundEntityReaperService : PeriodicBackgroundServiceBase
{
    private readonly IEntityReaper _reaper;
    private readonly IClockService _clock;
    private readonly EntityReaperConfig _config;
    private readonly ILogger<BackgroundEntityReaperService>? _logger;

    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(30);
    protected override TimeSpan Interval => _config.ScanInterval;
    protected override IClockService Clock => _clock;
    protected override ILogger? Logger => _logger;
    protected override string ServiceName => "后台实体回收服务";

    public BackgroundEntityReaperService(
        IEntityReaper reaper,
        IClockService clock,
        EntityReaperConfig? config = null,
        ILogger<BackgroundEntityReaperService>? logger = null)
    {
        _reaper = reaper;
        _clock = clock;
        _config = config ?? new EntityReaperConfig();
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _reaper.ScanOnce();
        return Task.CompletedTask;
    }
}
