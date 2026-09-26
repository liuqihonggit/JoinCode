namespace Core.Agents;

/// <summary>
/// 进度跟踪器 — 记录工具使用次数、token 消耗、最近活动和摘要
/// </summary>
public sealed class ProgressTracker : JoinCode.Abstractions.Interfaces.IProgressTracker {
    private readonly IClockService? _clock;
    private readonly List<JoinCode.Abstractions.Interfaces.ToolActivity> _recentActivities = new(5);
    private readonly AsyncLock _recentActivitiesLock = new("ProgressTracker");
    private int _toolUseCount;
    private int _tokenCount;
    private string? _summary;
    private volatile bool _notified;

    /// <summary>
    /// 构造进度跟踪器
    /// </summary>
    /// <param name="clock">时钟服务（可选，用于测试时间控制）</param>
    public ProgressTracker(IClockService? clock = null) {
        _clock = clock;
    }

    /// <summary>工具使用总次数</summary>
    public int ToolUseCount => _toolUseCount;

    /// <summary>token 消耗总数</summary>
    public int TokenCount => _tokenCount;

    /// <summary>当前摘要文本</summary>
    public string? Summary => _summary;

    /// <summary>是否已通知（标记通知已发送）</summary>
    public bool Notified => _notified;

    /// <summary>
    /// 记录一次工具使用 — 累加计数并追加到最近活动列表（保留最近 5 条）
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="activityDescription">活动描述（可选）</param>
    /// <param name="input">输入参数字典（可选）</param>
    public void RecordToolUse(string toolName, string? activityDescription = null, Dictionary<string, string>? input = null) {
        Interlocked.Increment(ref _toolUseCount);

        var activity = new JoinCode.Abstractions.Interfaces.ToolActivity {
            ToolName = toolName,
            ActivityDescription = activityDescription,
            IsSearch = toolName.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0,
            IsRead = toolName.IndexOf("read", StringComparison.OrdinalIgnoreCase) >= 0,
            Input = input,
            Timestamp = _clock?.GetUtcNow() ?? DateTime.UtcNow
        };

        using (_recentActivitiesLock.LockOrCrash()) {
            if (_recentActivities.Count >= 5)
                _recentActivities.RemoveAt(0);
            _recentActivities.Add(activity);
        }
    }

    /// <summary>
    /// 记录 token 消耗 — 原子累加到总计数
    /// </summary>
    /// <param name="tokenCount">本次消耗的 token 数</param>
    public void RecordTokenUsage(int tokenCount) {
        Interlocked.Add(ref _tokenCount, tokenCount);
    }

    /// <summary>
    /// 更新摘要文本
    /// </summary>
    /// <param name="summary">新摘要文本</param>
    public void UpdateSummary(string summary) {
        _summary = summary;
    }

    /// <summary>
    /// 标记已通知 — 原子操作，仅首次调用返回 true
    /// </summary>
    /// <returns>是否首次标记（之前未通知则 true）</returns>
    public bool MarkNotified() {
        return Interlocked.CompareExchange(ref _notified, true, false) == false;
    }

    /// <summary>
    /// 转换为进度快照 — 包含工具次数、token 数、最近活动列表和摘要
    /// </summary>
    /// <returns>Agent 进度快照</returns>
    public JoinCode.Abstractions.Interfaces.AgentProgress ToProgress() {
        JoinCode.Abstractions.Interfaces.ToolActivity? lastActivity;
        IReadOnlyList<JoinCode.Abstractions.Interfaces.ToolActivity>? recentActivities;
        using (_recentActivitiesLock.LockOrCrash()) {
            lastActivity = _recentActivities.Count > 0
                ? _recentActivities[^1]
                : null;
            recentActivities = _recentActivities.ToList();
        }

        return new JoinCode.Abstractions.Interfaces.AgentProgress {
            ToolUseCount = _toolUseCount,
            TokenCount = _tokenCount,
            LastActivity = lastActivity,
            RecentActivities = recentActivities,
            Summary = _summary
        };
    }
}