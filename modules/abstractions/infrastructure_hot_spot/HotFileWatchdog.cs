namespace Infrastructure.HotSpot;

/// <summary>
/// 热文件监控兜底实现 — 热文件被改但未上报意图则告警
/// 队长改热文件不告警（队长有权改）；不增加认领计数
/// </summary>
[Register(typeof(IHotFileWatchdog), ServiceLifetime.Singleton)]
public sealed class HotFileWatchdog : IHotFileWatchdog {
    private readonly IHotFileDetector _hotFileDetector;
    private readonly IIntentCollector _intentCollector;
    private readonly IClockService _clock;

    /// <summary>
    /// 构造热文件监控器
    /// </summary>
    /// <param name="hotFileDetector">热文件检测器</param>
    /// <param name="intentCollector">意图收集器</param>
    /// <param name="clock">可选时钟服务，默认使用系统时钟</param>
    public HotFileWatchdog(IHotFileDetector hotFileDetector, IIntentCollector intentCollector, IClockService? clock = null) {
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
        _intentCollector = intentCollector ?? throw new ArgumentNullException(nameof(intentCollector));
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 检查单次文件变更是否违规(改热文件但未上报意图)
    /// </summary>
    /// <param name="filePath">被修改的文件路径</param>
    /// <param name="changerId">修改者标识</param>
    /// <param name="alert">违规时返回告警信息，合规或队长修改返回 null</param>
    /// <returns>true=需要告警；false=合规</returns>
    public bool TryGetChange(string filePath, string changerId, [NotNullWhen(true)] out HotFileAlert? alert) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(changerId);
        alert = null;

        if (changerId.Equals("captain", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!_hotFileDetector.IsHotFile(filePath))
            return false;

        var intents = _intentCollector.GetIntents(filePath);
        if (intents.Count > 0)
            return false;

        alert = new HotFileAlert {
            FilePath = filePath,
            ChangerId = changerId,
            AlertMessage = FormattableString.Invariant(
                $"Worker {changerId} 私自修改热文件 {filePath} 未上报意图，请检查"),
            AlertedAt = _clock.GetUtcNow()
        };
        return true;
    }

    /// <summary>
    /// 批量检查文件变更是否违规
    /// </summary>
    /// <param name="changes">文件变更列表(路径, 修改者标识)</param>
    /// <returns>所有违规变更的告警列表</returns>
    public IReadOnlyList<HotFileAlert> CheckChanges(IReadOnlyList<(string FilePath, string ChangerId)> changes) {
        ArgumentNullException.ThrowIfNull(changes);
        var alerts = new List<HotFileAlert>();
        foreach (var (filePath, changerId) in changes) {
            if (TryGetChange(filePath, changerId, out var alert))
                alerts.Add(alert);
        }
        return alerts;
    }
}