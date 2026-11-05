
namespace JoinCode.Abstractions.Interfaces;

public interface ITeammateObserver {
    /// <summary>获取当前正在运行的队友列表。</summary>
    Task<IReadOnlyList<TeammateInfo>> GetRunningTeammatesAsync();

    event EventHandler<TeammateChangedEventArgs>? TeammateChanged;
}

public sealed record TeammateInfo {
    /// <summary>获取队友标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取显示名称。</summary>
    public required string DisplayName { get; init; }
    /// <summary>获取旋转动词（进行时）。</summary>
    public required string SpinnerVerb { get; init; }
    /// <summary>获取颜色十六进制值。</summary>
    public required string ColorHex { get; init; }
    /// <summary>获取代理状态。</summary>
    public required AgentStatus State { get; init; }
    /// <summary>获取启动时间。</summary>
    public DateTime? StartedAt { get; init; }
    /// <summary>获取 token 计数。</summary>
    public long TokenCount { get; init; }
    /// <summary>获取工具使用次数。</summary>
    public int ToolUseCount { get; init; }
    /// <summary>获取最后活动描述。</summary>
    public string? LastActivity { get; init; }
    /// <summary>获取最近工具活动列表。</summary>
    public IReadOnlyList<ToolActivity>? RecentActivities { get; init; }
    /// <summary>获取过去时动词。</summary>
    public string? PastTenseVerb { get; init; }
    /// <summary>获取一个值，指示是否请求关闭。</summary>
    public bool ShutdownRequested { get; init; }
    /// <summary>获取一个值，指示是否等待计划审批。</summary>
    public bool AwaitingPlanApproval { get; init; }
    /// <summary>获取提示词。</summary>
    public string? Prompt { get; init; }
    /// <summary>获取预览行列表。</summary>
    public IReadOnlyList<string> PreviewLines { get; init; } = [];
    /// <summary>获取空闲开始时间。</summary>
    public DateTime? IdleStartedAt { get; init; }
}

public sealed class TeammateChangedEventArgs : EventArgs {
    /// <summary>获取代理标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取旧状态。</summary>
    public required AgentStatus OldState { get; init; }
    /// <summary>获取新状态。</summary>
    public required AgentStatus NewState { get; init; }
}