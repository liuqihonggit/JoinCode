namespace JoinCode.Abstractions.Interfaces;

public interface IForkSubAgentManager
{
    Task<ForkResult> ForkAsync(ForkOptions options, CancellationToken ct = default);

    Task<IReadOnlyList<ForkSubAgent>> GetActiveForksAsync(CancellationToken ct = default);

    Task<ForkResult> MergeForkAsync(string forkId, CancellationToken ct = default);

    Task CancelForkAsync(string forkId, CancellationToken ct = default);

    event EventHandler<ForkCompletedEventArgs>? ForkCompleted;
}

public sealed class ForkOptions
{
    public required string ParentSessionId { get; init; }
    public required string TaskDescription { get; init; }
    public bool ShareCache { get; init; } = true;
    public bool ShareContext { get; init; } = true;
    public string? SystemPrompt { get; init; }
    public int MaxIterations { get; init; } = 10;
    public JoinCode.Abstractions.Security.PermissionMode PermissionMode { get; init; } = JoinCode.Abstractions.Security.PermissionMode.Plan;
    public List<string>? AllowedTools { get; init; }
    public List<string>? DeniedTools { get; init; }
    public bool RunInBackground { get; init; }
    public int MaxForkDepth { get; init; } = 3;
    public MessageList? ParentMessageList { get; init; }
    public CacheSafeParams? CacheSafeParams { get; init; }
    public bool UseExactTools { get; init; } = true;

    /// <summary>
    /// 隔离模式 — 对齐 TS AgentTool isolation 参数
    /// Fork 路径下指定 worktree 隔离，让子智能体在独立工作树中执行
    /// </summary>
    public AgentIsolationMode IsolationMode { get; init; } = AgentIsolationMode.None;
}

public sealed class ForkSubAgent
{
    public required string ForkId { get; init; }
    public required string ParentSessionId { get; init; }
    public required ForkState State { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string? Result { get; init; }
}

public enum ForkState { Running, Completed, Merged, Cancelled, Failed }

public sealed class ForkResult
{
    public required string ForkId { get; init; }
    public required ForkState State { get; init; }
    public string? Result { get; init; }
    public Dictionary<string, string> SharedCache { get; init; } = new();
}

public sealed class ForkCompletedEventArgs : EventArgs
{
    public required string ForkId { get; init; }
    public required ForkState State { get; init; }
    public required string TaskDescription { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
    public string? WorktreePath { get; init; }
}
