namespace Infrastructure.HotSpot;

/// <summary>
/// 热点追踪器实现 — 基于 IntentCollector + IHotFileDetector 判断热点
/// 热文件 contract_claim>=1 即归队长；非热文件 contract_claim>=3 才触发
/// internal_claim 不触发；队长修改不计入认领集合
/// </summary>
[Register(typeof(IHotSpotTracker))]
public sealed class HotSpotTracker : IHotSpotTracker
{
    private readonly IIntentCollector _intentCollector;
    private readonly IHotFileDetector _hotFileDetector;
    private int _hotFileThreshold = 1;
    private int _normalFileThreshold = 3;

    public HotSpotTracker(IIntentCollector intentCollector, IHotFileDetector hotFileDetector)
    {
        _intentCollector = intentCollector ?? throw new ArgumentNullException(nameof(intentCollector));
        _hotFileDetector = hotFileDetector ?? throw new ArgumentNullException(nameof(hotFileDetector));
    }

    public bool IsHotSpot(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return GetHotSpotInfo(filePath).IsHotSpot;
    }

    public IReadOnlyList<string> GetHotSpotFiles()
    {
        var allIntents = _intentCollector.GetAllIntents();
        var candidateFiles = allIntents
            .Where(i => !i.IsFromCaptain)
            .Select(i => i.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hotSpots = new List<string>();
        foreach (var file in candidateFiles)
        {
            if (GetHotSpotInfo(file).IsHotSpot)
                hotSpots.Add(file);
        }
        return hotSpots;
    }

    public HotSpotInfo GetHotSpotInfo(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var intents = _intentCollector.GetIntents(filePath);
        var nonCaptainIntents = intents.Where(i => !i.IsFromCaptain).ToList();

        var contractWorkers = nonCaptainIntents
            .Where(i => i.Intent == ModifyIntent.ContractChange)
            .Select(i => i.WorkerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var internalWorkers = nonCaptainIntents
            .Where(i => i.Intent == ModifyIntent.InternalChange)
            .Select(i => i.WorkerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var isHotFile = _hotFileDetector.IsHotFile(filePath);
        var threshold = isHotFile ? _hotFileThreshold : _normalFileThreshold;
        var isHotSpot = contractWorkers.Count >= threshold;

        return new HotSpotInfo
        {
            FilePath = filePath,
            ContractClaimCount = contractWorkers.Count,
            InternalClaimCount = internalWorkers.Count,
            IsHotFile = isHotFile,
            IsHotSpot = isHotSpot,
            ClaimingWorkers = contractWorkers
        };
    }

    public void SetThresholds(int hotFileThreshold, int normalFileThreshold)
    {
        if (hotFileThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(hotFileThreshold), "热文件阈值必须 >= 1");
        if (normalFileThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(normalFileThreshold), "非热文件阈值必须 >= 1");

        Interlocked.Exchange(ref _hotFileThreshold, hotFileThreshold);
        Interlocked.Exchange(ref _normalFileThreshold, normalFileThreshold);
    }

    public void Clear()
    {
        var allIntents = _intentCollector.GetAllIntents();
        var workers = allIntents.Select(i => i.WorkerId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var worker in workers)
        {
            _intentCollector.RemoveWorkerAsync(worker).Wait();
        }
    }
}
