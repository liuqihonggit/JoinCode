namespace Infrastructure.HotSpot;

/// <summary>
/// 文件写入监听器 — Worker 改文件时自动上报意图到 IntentCollector
/// 热文件（接口/枚举/公共签名）→ ContractChange；非热文件 → InternalChange
/// 队长（mainAgent）的修改标记为 IsFromCaptain 不计入热点认领
/// </summary>
public sealed class IntentReportFileWriteListener : IFileWriteListener
{
    private readonly IIntentCollector _intentCollector;
    private readonly IHotFileDetector _hotFileDetector;
    private readonly string _captainId;
    private readonly ILogger<IntentReportFileWriteListener>? _logger;

    public IntentReportFileWriteListener(
        IIntentCollector intentCollector,
        IHotFileDetector hotFileDetector,
        string captainId,
        ILogger<IntentReportFileWriteListener>? logger = null)
    {
        _intentCollector = intentCollector ?? throw new ArgumentNullException(nameof(intentCollector));
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
        _captainId = captainId ?? throw new ArgumentNullException(nameof(captainId));
        _logger = logger;
    }

    public void OnFileWrite(FileWriteEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var isHotFile = _hotFileDetector.IsHotFile(e.FilePath);
        var isFromCaptain = string.Equals(e.AgentId, _captainId, StringComparison.OrdinalIgnoreCase);
        var intent = isHotFile ? ModifyIntent.ContractChange : ModifyIntent.InternalChange;
        var workerId = isFromCaptain ? "captain" : e.AgentId;

        var fileIntent = new FileModifyIntent
        {
            FilePath = e.FilePath,
            Intent = intent,
            WorkerId = workerId,
            ReportedAt = DateTimeOffset.UtcNow,
        };

        _ = ReportAsync(workerId, fileIntent);
        _logger?.LogDebug("[IntentReport] {AgentId} 改 {FilePath} → {Intent} (HotFile={IsHotFile})", e.AgentId, e.FilePath, intent, isHotFile);
    }

    private async Task ReportAsync(string workerId, FileModifyIntent intent)
    {
        try
        {
            await _intentCollector.ReportAsync(workerId, [intent]).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("[IntentReport] 上报意图失败: {Message}", ex.Message);
        }
    }
}
