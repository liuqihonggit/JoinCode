namespace Infrastructure.HotSpot;

/// <summary>
/// 契约变更广播监听器 — 队长改热文件后自动广播 ContractChanged 给依赖 Worker
/// 仅队长改热文件时触发，Worker 改文件不触发
/// </summary>
public sealed class ContractChangeBroadcastListener : IFileWriteListener
{
    private readonly IContractChangeBroadcaster _broadcaster;
    private readonly IHotFileDetector _hotFileDetector;
    private readonly IHotSpotTracker _hotSpotTracker;
    private readonly IContractChangeNotificationRouter _router;
    private readonly string _captainId;
    private readonly ILogger<ContractChangeBroadcastListener>? _logger;

    public ContractChangeBroadcastListener(
        IContractChangeBroadcaster broadcaster,
        IHotFileDetector hotFileDetector,
        IHotSpotTracker hotSpotTracker,
        IContractChangeNotificationRouter router,
        string captainId,
        ILogger<ContractChangeBroadcastListener>? logger = null)
    {
        _broadcaster = broadcaster ?? throw new ArgumentNullException(nameof(broadcaster));
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
        _hotSpotTracker = hotSpotTracker ?? throw new ArgumentNullException(nameof(hotSpotTracker));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _captainId = captainId ?? throw new ArgumentNullException(nameof(captainId));
        _logger = logger;
    }

    public void OnFileWrite(FileWriteEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var isCaptain = string.Equals(e.AgentId, _captainId, StringComparison.OrdinalIgnoreCase);
        if (!isCaptain) return;

        var isHotFile = _hotFileDetector.IsHotFile(e.FilePath);
        if (!isHotFile) return;

        var dependentWorkers = _hotSpotTracker.GetHotSpotInfo(e.FilePath).ClaimingWorkers;
        if (dependentWorkers.Count == 0) return;

        var notification = $"队长改热文件 {e.FilePath} 契约变更，请 git pull --rebase 同步主干后继续";
        _router.EnqueueNotifications(dependentWorkers, notification);
        _ = BroadcastAsync(e.FilePath, dependentWorkers);
        _logger?.LogInformation("[ContractBroadcast] 队长改热文件 {FilePath}，广播通知 {Count} 个 Worker", e.FilePath, dependentWorkers.Count);
    }

    private async Task BroadcastAsync(string filePath, IReadOnlyList<string> workers)
    {
        try
        {
            await _broadcaster.BroadcastContractChangeAsync(_captainId, filePath, workers).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("[ContractBroadcast] 广播失败: {Message}", ex.Message);
        }
    }
}
