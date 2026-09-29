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
public sealed class BackgroundAgentItemVm {
    private static readonly FrozenSet<string> RunningStates = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "running", "pending", "paused");

    /// <summary>子代理 ID</summary>
    public string AgentId { get; }
    /// <summary>代理名称</summary>
    public string Name { get; }
    /// <summary>代理描述</summary>
    public string Description { get; }
    /// <summary>运行状态文本</summary>
    public string State { get; }
    /// <summary>启动时间（null=未启动）</summary>
    public DateTime? StartedAt { get; }
    /// <summary>工具调用次数</summary>
    public int ToolUseCount { get; }
    /// <summary>token 消耗总量</summary>
    public long TokenCount { get; }

    /// <summary>是否仍在运行（驱动终止按钮可见性）</summary>
    public bool IsRunning { get; }

    /// <summary>是否可暂停 — running/pending 状态显示暂停按钮</summary>
    public bool CanPause { get; }

    /// <summary>是否可恢复 — paused 状态显示继续按钮</summary>
    public bool CanResume { get; }

    /// <summary>是否展开 — 控制实时输出流区域显隐</summary>
    public bool IsExpanded { get; set; }

    /// <summary>最近活动摘要（展开时从 SubAgentRunTracker 填充）</summary>
    public string? LastActivityText { get; set; }

    /// <summary>尾部可见活动列表（展开时从 SubAgentRunTracker 填充）</summary>
    public IReadOnlyList<string> VisibleActivities { get; set; } = [];

    /// <summary>已运行时长展示文本</summary>
    public string ElapsedText { get; }
    /// <summary>统计摘要文本（工具次数 · token 数）</summary>
    public string StatsText { get; }

    /// <summary>初始化 BackgroundAgentItemVm 实例</summary>
    public BackgroundAgentItemVm(BackgroundAgentInfo info) {
        AgentId = info.AgentId;
        Name = info.Name;
        Description = info.Description;
        State = info.State;
        StartedAt = info.StartedAt;
        ToolUseCount = info.ToolUseCount;
        TokenCount = info.TokenCount;
        IsRunning = RunningStates.Contains(info.State);
        CanPause = string.Equals(info.State, "running", StringComparison.OrdinalIgnoreCase)
                || string.Equals(info.State, "pending", StringComparison.OrdinalIgnoreCase);
        CanResume = string.Equals(info.State, "paused", StringComparison.OrdinalIgnoreCase);

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
public sealed partial class BackgroundAgentsPanelViewModel : ObservableObject {
    private readonly Func<CancellationToken, Task<IReadOnlyList<BackgroundAgentInfo>>> _fetcher;
    private readonly Func<string, CancellationToken, Task<bool>> _stopper;
    private readonly Func<string, CancellationToken, Task<bool>>? _pauser;
    private readonly Func<string, CancellationToken, Task<bool>>? _resumer;
    private SubAgentRunTracker? _runTracker;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>后台代理行集合</summary>
    public System.Collections.ObjectModel.ObservableCollection<BackgroundAgentItemVm> Items { get; } = [];

    /// <summary>初始化 BackgroundAgentsPanelViewModel 实例</summary>
    public BackgroundAgentsPanelViewModel(
        Func<CancellationToken, Task<IReadOnlyList<BackgroundAgentInfo>>> fetcher,
        Func<string, CancellationToken, Task<bool>> stopper,
        Func<string, CancellationToken, Task<bool>>? pauser = null,
        Func<string, CancellationToken, Task<bool>>? resumer = null,
        SubAgentRunTracker? runTracker = null) {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _stopper = stopper ?? throw new ArgumentNullException(nameof(stopper));
        _pauser = pauser;
        _resumer = resumer;
        _runTracker = runTracker;
    }

    /// <summary>更新子代理运行追踪器 — 每回合 ChatTurnProcessor 重建 tracker 后同步引用</summary>
    public void UpdateTracker(SubAgentRunTracker? tracker) => _runTracker = tracker;

    /// <summary>pill 点击：关闭时打开并刷新；已打开时仅收起（不重复拉取）</summary>
    [RelayCommand]
    public async Task ToggleAndRefreshAsync() {
        if (IsOpen) {
            IsOpen = false;
            return;
        }
        IsOpen = true;
        await RefreshAsync();
    }

    /// <summary>拉取引擎运行列表并重建行集合</summary>
    [RelayCommand]
    public async Task RefreshAsync() {
        var snapshot = await _fetcher(CancellationToken.None);
        ApplySnapshot(snapshot);
    }

    /// <summary>终止后台代理 — 成功后立即刷新剔除该行；引擎拒绝则保留等待下次刷新</summary>
    [RelayCommand]
    public async Task StopAsync(string? agentId) {
        if (string.IsNullOrEmpty(agentId))
            return;
        var stopped = await _stopper(agentId, CancellationToken.None);
        if (stopped)
            await RefreshAsync();
    }

    /// <summary>暂停子代理 — 成功后刷新状态</summary>
    [RelayCommand]
    public async Task PauseAsync(string? agentId) {
        if (string.IsNullOrEmpty(agentId) || _pauser is null)
            return;
        if (await _pauser(agentId, CancellationToken.None))
            await RefreshAsync();
    }

    /// <summary>恢复子代理 — 成功后刷新状态</summary>
    [RelayCommand]
    public async Task ResumeAsync(string? agentId) {
        if (string.IsNullOrEmpty(agentId) || _resumer is null)
            return;
        if (await _resumer(agentId, CancellationToken.None))
            await RefreshAsync();
    }

    /// <summary>暂停所有运行中子代理 — 逐个暂停后刷新</summary>
    [RelayCommand]
    public async Task PauseAllAsync() {
        if (_pauser is null) return;
        foreach (var item in Items) {
            if (item.CanPause)
                await _pauser(item.AgentId, CancellationToken.None);
        }
        await RefreshAsync();
    }

    /// <summary>恢复所有暂停中子代理 — 逐个恢复后刷新</summary>
    [RelayCommand]
    public async Task ResumeAllAsync() {
        if (_resumer is null) return;
        foreach (var item in Items) {
            if (item.CanResume)
                await _resumer(item.AgentId, CancellationToken.None);
        }
        await RefreshAsync();
    }

    /// <summary>终止所有运行中子代理 — 逐个终止后刷新</summary>
    [RelayCommand]
    public async Task StopAllAsync() {
        foreach (var item in Items) {
            if (item.IsRunning)
                await _stopper(item.AgentId, CancellationToken.None);
        }
        await RefreshAsync();
    }

    /// <summary>展开/收起子代理卡片 — 展开时从 tracker 填充活动数据到 ItemVm</summary>
    [RelayCommand]
    public void ToggleExpand(string? agentId) {
        if (string.IsNullOrEmpty(agentId)) return;
        var item = Items.FirstOrDefault(i => i.AgentId == agentId);
        if (item is null) return;
        item.IsExpanded = !item.IsExpanded;
        if (item.IsExpanded) FillActivities(item);
    }

    /// <summary>从 SubAgentRunTracker 填充单个 ItemVm 的活动数据</summary>
    private void FillActivities(BackgroundAgentItemVm item) {
        if (_runTracker is null) return;
        var run = _runTracker.Runs.FirstOrDefault(r => r.AgentId == item.AgentId);
        if (run is null) return;
        item.LastActivityText = run.LastActivityText;
        item.VisibleActivities = run.VisibleActivities;
    }

    /// <summary>面板快照应用事件 — MainViewModel 据此同步 RunStatus 后台计数</summary>
    public event Action<int>? SnapshotApplied;

    /// <summary>用快照重建行集合（刷新与测试共用入口）</summary>
    public void ApplySnapshot(IReadOnlyList<BackgroundAgentInfo> snapshot) {
        Items.Clear();
        foreach (var info in snapshot)
            Items.Add(new BackgroundAgentItemVm(info));
        CountText = snapshot.Count > 0 ? $"{snapshot.Count} 个后台代理" : string.Empty;
        SnapshotApplied?.Invoke(snapshot.Count);
    }
}