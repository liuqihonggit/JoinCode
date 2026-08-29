namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 后台代理快照 — 引擎运行列表的 GUI 投影（会话门面映射产物）
/// </summary>
public sealed record BackgroundAgentInfo(
    string AgentId,
    string Name,
    string Description,
    string State,
    DateTime? StartedAt,
    int ToolUseCount,
    long TokenCount);

/// <summary>
/// 后台代理行 VM — 管理面板单行（状态/耗时/统计/终止按钮可见性）
/// </summary>
public sealed class BackgroundAgentItemVm
{
    private static readonly FrozenSet<string> RunningStates = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "running", "pending", "paused");

    public string AgentId { get; }
    public string Name { get; }
    public string Description { get; }
    public string State { get; }
    public DateTime? StartedAt { get; }
    public int ToolUseCount { get; }
    public long TokenCount { get; }

    /// <summary>是否仍在运行（驱动终止按钮可见性）</summary>
    public bool IsRunning { get; }

    public string ElapsedText { get; }
    public string StatsText { get; }

    public BackgroundAgentItemVm(BackgroundAgentInfo info)
    {
        AgentId = info.AgentId;
        Name = info.Name;
        Description = info.Description;
        State = info.State;
        StartedAt = info.StartedAt;
        ToolUseCount = info.ToolUseCount;
        TokenCount = info.TokenCount;
        IsRunning = RunningStates.Contains(info.State);

        ElapsedText = info.StartedAt is { } started
            ? FormatElapsed(DateTime.Now - started)
            : "—";
        StatsText = $"{ToolUseCount} 次工具 · {FormatTokens(TokenCount)}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s"
            : $"{elapsed.TotalSeconds:F0}s";

    private static string FormatTokens(long tokens)
        => tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : tokens.ToString();
}

/// <summary>
/// 后台代理管理面板 VM — pill 点击开合 + 引擎快照刷新 + 终止命令。
/// 数据源经委托注入（fetcher/stopper），由 JccChatSession 绑定到
/// IAgentService.GetRunningAgentsAsync / StopAgentAsync（fork 由其内部归并）。
/// 直接读引擎权威列表，天然覆盖 fork 跨回合生命周期。
/// </summary>
public sealed partial class BackgroundAgentsPanelViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<BackgroundAgentInfo>>> _fetcher;
    private readonly Func<string, CancellationToken, Task<bool>> _stopper;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _countText = string.Empty;

    public System.Collections.ObjectModel.ObservableCollection<BackgroundAgentItemVm> Items { get; } = [];

    public BackgroundAgentsPanelViewModel(
        Func<CancellationToken, Task<IReadOnlyList<BackgroundAgentInfo>>> fetcher,
        Func<string, CancellationToken, Task<bool>> stopper)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _stopper = stopper ?? throw new ArgumentNullException(nameof(stopper));
    }

    /// <summary>pill 点击：关闭时打开并刷新；已打开时仅收起（不重复拉取）</summary>
    [RelayCommand]
    public async Task ToggleAndRefreshAsync()
    {
        if (IsOpen)
        {
            IsOpen = false;
            return;
        }
        IsOpen = true;
        await RefreshAsync();
    }

    /// <summary>拉取引擎运行列表并重建行集合</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var snapshot = await _fetcher(CancellationToken.None);
        ApplySnapshot(snapshot);
    }

    /// <summary>终止后台代理 — 成功后立即刷新剔除该行；引擎拒绝则保留等待下次刷新</summary>
    [RelayCommand]
    public async Task StopAsync(string? agentId)
    {
        if (string.IsNullOrEmpty(agentId))
            return;
        var stopped = await _stopper(agentId, CancellationToken.None);
        if (stopped)
            await RefreshAsync();
    }

    /// <summary>面板快照应用事件 — MainViewModel 据此同步 RunStatus 后台计数</summary>
    public event Action<int>? SnapshotApplied;

    /// <summary>用快照重建行集合（刷新与测试共用入口）</summary>
    public void ApplySnapshot(IReadOnlyList<BackgroundAgentInfo> snapshot)
    {
        Items.Clear();
        foreach (var info in snapshot)
            Items.Add(new BackgroundAgentItemVm(info));
        CountText = snapshot.Count > 0 ? $"{snapshot.Count} 个后台代理" : string.Empty;
        SnapshotApplied?.Invoke(snapshot.Count);
    }
}
