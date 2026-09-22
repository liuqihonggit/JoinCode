namespace Infrastructure.HotSpot;

/// <summary>
/// 文件写入监听器 — Worker 改文件时自动上报意图到 IntentCollector
/// 热文件（接口/枚举/公共签名）→ ContractChange；非热文件 → InternalChange
/// 队长（mainAgent）的修改标记为 IsFromCaptain 不计入热点认领
/// </summary>
public sealed class IntentReportFileWriteListener : IFileWriteListener {
    private readonly IIntentCollector _intentCollector;
    private readonly IHotFileDetector _hotFileDetector;
    private readonly string _captainId;
    private readonly ILogger<IntentReportFileWriteListener>? _logger;
    private readonly ConcurrentBag<Task> _pendingReports = new();

    /// <summary>
    /// 构造文件写入监听器
    /// </summary>
    /// <param name="intentCollector">意图收集器</param>
    /// <param name="hotFileDetector">热文件检测器</param>
    /// <param name="captainId">队长（mainAgent）ID，其修改标记为 IsFromCaptain</param>
    /// <param name="logger">日志记录器</param>
    public IntentReportFileWriteListener(
        IIntentCollector intentCollector,
        IHotFileDetector hotFileDetector,
        string captainId,
        ILogger<IntentReportFileWriteListener>? logger = null) {
        _intentCollector = intentCollector ?? throw new ArgumentNullException(nameof(intentCollector));
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
        _captainId = captainId ?? throw new ArgumentNullException(nameof(captainId));
        _logger = logger;
    }

    /// <summary>
    /// 文件写入事件处理 — 根据热文件判定意图类型并上报到 IntentCollector
    /// </summary>
    /// <param name="e">文件写入事件参数</param>
    public void OnFileWrite(FileWriteEventArgs e) {
        ArgumentNullException.ThrowIfNull(e);

        var isHotFile = _hotFileDetector.IsHotFile(e.FilePath);
        var isFromCaptain = string.Equals(e.AgentId, _captainId, StringComparison.OrdinalIgnoreCase);
        var intent = isHotFile ? ModifyIntent.ContractChange : ModifyIntent.InternalChange;
        var workerId = isFromCaptain ? "captain" : e.AgentId;

        var fileIntent = new FileModifyIntent {
            FilePath = e.FilePath,
            Intent = intent,
            WorkerId = workerId,
            ReportedAt = DateTimeOffset.UtcNow,
        };

        _pendingReports.Add(ReportAsync(workerId, fileIntent));
        _logger?.LogDebug("[IntentReport] {AgentId} 改 {FilePath} → {Intent} (HotFile={IsHotFile})", e.AgentId, e.FilePath, intent, isHotFile);
    }

    /// <summary>
    /// 等待所有 pending 上报任务完成 — 调用方可选 await 以确保上报落盘
    /// </summary>
    public Task WaitForPendingReportsAsync() => Task.WhenAll(_pendingReports);

    private async Task ReportAsync(string workerId, FileModifyIntent intent) {
        try {
            await _intentCollector.ReportAsync(workerId, [intent]).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning("[IntentReport] 上报意图失败: {Message}", ex.Message);
        }
    }
}