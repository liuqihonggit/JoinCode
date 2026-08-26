namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 子代理运行态 — SubAgentRunTracker 聚合的单个 subAgent 运行记录，
/// 字段直接驱动 AgentRunPanelView 绑定（状态点/徽章/当前活动/统计）。
/// </summary>
public sealed class SubAgentRun
{
    /// <summary>子代理唯一 ID（引擎 AgentStreamChunk.AgentId）</summary>
    public required string AgentId { get; init; }

    /// <summary>显示名（如 "explore"；未命名时回退角色值）</summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>任务描述</summary>
    public string Description { get; internal set; } = string.Empty;

    /// <summary>角色标识（executor/coordinator/...，驱动徽章配色）</summary>
    public string Role { get; internal set; } = string.Empty;

    /// <summary>运行状态机：Running → Completed / Failed</summary>
    public SubAgentRunState State { get; internal set; } = SubAgentRunState.Running;

    /// <summary>是否成功（仅终态有意义）</summary>
    public bool IsSuccess { get; internal set; }

    /// <summary>工具调用次数（ToolCallEnd 计数）</summary>
    public int ToolUseCount { get; internal set; }

    /// <summary>执行时长（毫秒；引擎 Complete 块携带）</summary>
    public long? ExecutionTimeMs { get; internal set; }

    /// <summary>最终输出（成功时的 Complete 内容/失败时的错误消息，供 Transcript 回放）</summary>
    public string? FinalOutput { get; internal set; }

    /// <summary>最近一条活动摘要文本（当前正在做什么）</summary>
    public string LastActivityText { get; internal set; } = "Initializing…";

    /// <summary>当前连续搜索/读取类工具的次数（非搜索类活动归零，驱动折叠摘要计数）</summary>
    public int SearchReadStreak { get; internal set; }

    /// <summary>尾部可见活动（环形缓冲，默认 3 条）</summary>
    public IReadOnlyList<string> VisibleActivities => _visibleActivities;

    /// <summary>被尾部缓冲挤出的活动数（驱动 "+N more" 折叠提示）</summary>
    public int HiddenActivityCount { get; internal set; }

    /// <summary>开始时间（本地），驱动运行中耗时显示</summary>
    public DateTime StartedAtLocal { get; } = DateTime.Now;

    internal readonly List<string> _visibleActivities = [];

    private readonly object _transcriptLock = new();
    private readonly List<SubAgentTranscriptItem> _transcript = [];

    /// <summary>完整时间线（回放窗口数据源，不裁剪；线程安全追加）</summary>
    public IReadOnlyList<SubAgentTranscriptItem> Transcript
    {
        get { lock (_transcriptLock) return [.. _transcript]; }
    }

    /// <summary>追加时间线条目（tracker 专用）</summary>
    internal void AppendTranscript(string glyph, string text)
    {
        lock (_transcriptLock)
            _transcript.Add(new SubAgentTranscriptItem(DateTime.Now, glyph, text));
    }
}

/// <summary>子代理时间线条目 — 回放窗口的行模型（glyph: ▶ 调用 / ✓ 成功 / ✗ 失败 / ¶ 正文 / ■ 终态）</summary>
public sealed record SubAgentTranscriptItem(DateTime At, string Glyph, string Text);

/// <summary>子代理运行状态机</summary>
public enum SubAgentRunState
{
    Running,
    Completed,
    Failed
}
