namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 子代理运行行 VM — 包装 <see cref="SubAgentRun"/> 供 XAML 绑定。
/// Refresh() 从 run 拉平快照（状态点/标题/统计/活动行），由 MainViewModel 在事件到达时调用；
/// 三态布尔驱动三个静态着色的状态点 TextBlock，避免动态画刷键解析。
/// </summary>
public sealed class AgentRunVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>聚合器持有的运行记录（数据源）</summary>
    public SubAgentRun Run { get; }

    /// <summary>子代理 ID（测试定位用）</summary>
    public string AgentId => Run.AgentId;

    /// <summary>已解析的 worktree 目录（懒解析缓存；null=未知，""=确认未启用）</summary>
    public string? WorktreePath { get; private set; }

    /// <summary>tracker 专用：记录解析结果避免重复查询</summary>
    public void SetWorktreePath(string path) => WorktreePath = path;

    private string _stateGlyph = "●";
    public string StateGlyph { get => _stateGlyph; private set { if (_stateGlyph != value) { _stateGlyph = value; Raise(nameof(StateGlyph)); } } }

    private string _headerText = string.Empty;
    public string HeaderText { get => _headerText; private set { if (_headerText != value) { _headerText = value; Raise(nameof(HeaderText)); } } }

    private string _statsText = string.Empty;
    public string StatsText { get => _statsText; private set { if (_statsText != value) { _statsText = value; Raise(nameof(StatsText)); } } }

    private string _hiddenText = string.Empty;
    public string HiddenText { get => _hiddenText; private set { if (_hiddenText != value) { _hiddenText = value; Raise(nameof(HiddenText)); } } }

    private bool _isRunning = true;
    public bool IsRunning { get => _isRunning; private set { if (_isRunning != value) { _isRunning = value; Raise(nameof(IsRunning)); } } }

    private bool _isCompleted;
    public bool IsCompleted { get => _isCompleted; private set { if (_isCompleted != value) { _isCompleted = value; Raise(nameof(IsCompleted)); } } }

    private bool _isFailed;
    public bool IsFailed { get => _isFailed; private set { if (_isFailed != value) { _isFailed = value; Raise(nameof(IsFailed)); } } }

    /// <summary>尾部活动行（Refresh 时整体重建 — 行数固定 ≤3，重建成本可忽略）</summary>
    private ObservableCollection<string> _activityLines = [];
    public ObservableCollection<string> ActivityLines => _activityLines;

    public AgentRunVm(SubAgentRun run)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
        Refresh();
    }

    /// <summary>从运行记录拉平最新快照到绑定属性</summary>
    public void Refresh()
    {
        StateGlyph = Run.State switch
        {
            SubAgentRunState.Completed => "✓",
            SubAgentRunState.Failed => "✗",
            _ => "●"
        };
        IsRunning = Run.State == SubAgentRunState.Running;
        IsCompleted = Run.State == SubAgentRunState.Completed;
        IsFailed = Run.State == SubAgentRunState.Failed;

        HeaderText = string.IsNullOrEmpty(Run.Description)
            ? Run.Name
            : $"{Run.Name} — {Run.Description}";

        StatsText = Run.State switch
        {
            SubAgentRunState.Running when Run.ToolUseCount > 0 => $"{Run.ToolUseCount} 次工具调用",
            SubAgentRunState.Running => "启动中…",
            _ => $"完成 ({Run.ToolUseCount} 次工具调用{FormatDurationSuffix()})"
        };

        HiddenText = Run.HiddenActivityCount > 0 ? $"+{Run.HiddenActivityCount} 更多 ▸" : string.Empty;

        _activityLines.Clear();
        foreach (var line in Run.VisibleActivities)
            _activityLines.Add(line);
    }

    /// <summary>时长后缀：终态且有时长才显示 " · 2m 12s"</summary>
    private string FormatDurationSuffix()
    {
        if (Run.ExecutionTimeMs is not { } ms)
            return Run.IsSuccess ? "" : " · 失败";
        var elapsed = TimeSpan.FromMilliseconds(ms);
        var text = elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s"
            : $"{elapsed.TotalSeconds:F1}s";
        return $" · {text}";
    }
}
