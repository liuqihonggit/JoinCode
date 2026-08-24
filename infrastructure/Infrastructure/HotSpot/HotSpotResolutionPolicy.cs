namespace Infrastructure.HotSpot;

/// <summary>
/// 热点处置策略实现 — 基于 HotSpotTracker 判断，生成队长接管+Worker通知决策
/// 纯逻辑不执行实际通知，执行由中间件接入
/// </summary>
[Register(typeof(IHotSpotResolutionPolicy), ServiceLifetime.Singleton)]
public sealed class HotSpotResolutionPolicy : IHotSpotResolutionPolicy
{
    private readonly IHotSpotTracker _hotSpotTracker;

    public HotSpotResolutionPolicy(IHotSpotTracker hotSpotTracker)
    {
        _hotSpotTracker = hotSpotTracker ?? throw new ArgumentNullException(nameof(hotSpotTracker));
    }

    public HotSpotResolution Resolve(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var info = _hotSpotTracker.GetHotSpotInfo(filePath);

        if (!info.IsHotSpot)
        {
            return new HotSpotResolution
            {
                FilePath = filePath,
                ShouldCaptainTakeOver = false,
                WorkersToNotify = [],
                NotificationMessage = string.Empty
            };
        }

        return new HotSpotResolution
        {
            FilePath = filePath,
            ShouldCaptainTakeOver = true,
            WorkersToNotify = info.ClaimingWorkers,
            NotificationMessage = FormattableString.Invariant(
                $"热点文件 {filePath} 已由队长接管，请停止契约修改，git commit 半成品后继续内部修改")
        };
    }

    public IReadOnlyList<HotSpotResolution> ResolveAll()
    {
        var hotSpotFiles = _hotSpotTracker.GetHotSpotFiles();
        var resolutions = new List<HotSpotResolution>(hotSpotFiles.Count);
        foreach (var file in hotSpotFiles)
        {
            resolutions.Add(Resolve(file));
        }
        return resolutions;
    }
}
