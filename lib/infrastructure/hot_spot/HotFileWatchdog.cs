namespace Infrastructure.HotSpot;

/// <summary>
/// 热文件监控兜底实现 — 热文件被改但未上报意图则告警
/// 队长改热文件不告警（队长有权改）；不增加认领计数
/// </summary>
[Register(typeof(IHotFileWatchdog), ServiceLifetime.Singleton)]
public sealed class HotFileWatchdog : IHotFileWatchdog
{
    private readonly IHotFileDetector _hotFileDetector;
    private readonly IIntentCollector _intentCollector;
    private readonly IClockService _clock;

    public HotFileWatchdog(IHotFileDetector hotFileDetector, IIntentCollector intentCollector, IClockService? clock = null)
    {
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
        _intentCollector = intentCollector ?? throw new ArgumentNullException(nameof(intentCollector));
        _clock = clock ?? SystemClockService.Instance;
    }

    public HotFileAlert? CheckChange(string filePath, string changerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(changerId);

        if (changerId.Equals("captain", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!_hotFileDetector.IsHotFile(filePath))
            return null;

        var intents = _intentCollector.GetIntents(filePath);
        if (intents.Count > 0)
            return null;

        return new HotFileAlert
        {
            FilePath = filePath,
            ChangerId = changerId,
            AlertMessage = FormattableString.Invariant(
                $"Worker {changerId} 私自修改热文件 {filePath} 未上报意图，请检查"),
            AlertedAt = _clock.GetUtcNow()
        };
    }

    public IReadOnlyList<HotFileAlert> CheckChanges(IReadOnlyList<(string FilePath, string ChangerId)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var alerts = new List<HotFileAlert>();
        foreach (var (filePath, changerId) in changes)
        {
            var alert = CheckChange(filePath, changerId);
            if (alert is not null)
                alerts.Add(alert);
        }
        return alerts;
    }
}
