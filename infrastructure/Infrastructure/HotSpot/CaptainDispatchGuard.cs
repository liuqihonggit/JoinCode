namespace Infrastructure.HotSpot;

/// <summary>
/// 队长派发守卫实现 — 派发前查热点表，热点文件契约改队长自己揽
/// 纯逻辑不执行实际派发
/// </summary>
[Register(typeof(ICaptainDispatchGuard))]
public sealed class CaptainDispatchGuard : ICaptainDispatchGuard
{
    private readonly IHotSpotTracker _hotSpotTracker;

    public CaptainDispatchGuard(IHotSpotTracker hotSpotTracker)
    {
        _hotSpotTracker = hotSpotTracker ?? throw new ArgumentNullException(nameof(hotSpotTracker));
    }

    public DispatchDecision CheckBeforeDispatch(IReadOnlyList<string> taskFiles)
    {
        ArgumentNullException.ThrowIfNull(taskFiles);

        var hotSpotFiles = new List<string>();
        foreach (var file in taskFiles)
        {
            if (_hotSpotTracker.IsHotSpot(file))
                hotSpotFiles.Add(file);
        }

        if (hotSpotFiles.Count == 0)
        {
            return new DispatchDecision
            {
                ShouldCaptainHandle = false,
                Reason = "无热点文件，可派发给Worker",
                HotSpotFiles = []
            };
        }

        return new DispatchDecision
        {
            ShouldCaptainHandle = true,
            Reason = FormattableString.Invariant(
                $"任务涉及热点文件 {string.Join(", ", hotSpotFiles)}，队长自己揽不派Worker"),
            HotSpotFiles = hotSpotFiles
        };
    }
}
