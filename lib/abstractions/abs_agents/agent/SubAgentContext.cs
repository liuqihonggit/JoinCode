namespace JoinCode.Abstractions.Interfaces;

public sealed class SubAgentContext
{
    private static readonly AsyncLocal<SubAgentContext?> _current = new();

    public static SubAgentContext? Current => _current.Value;

    public required string AgentId { get; init; }
    public required AgentRole Role { get; init; }
    public ExecutorVariant? Variant { get; init; }
    public required string Task { get; init; }
    public string? ParentAgentId { get; set; }
    public string? SessionId { get; set; }
    public string? WorktreePath { get; set; }
    public string? CwdOverride { get; set; }
    public string? TeamId { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public int ToolCallCount { get; set; }
    public TokenUsage TokenUsage { get; } = new();
    public AgentStatus Status { get; set; } = AgentStatus.Pending;

    public IEnumerable<string> AllowedTools { get; init; } = [];
    public IEnumerable<string> DeniedTools { get; init; } = [];

    public string? SubagentName { get; init; }
    public bool IsBuiltIn { get; init; }
    public string? InvokingRequestId { get; set; }
    public bool InvocationEmitted { get; set; }
    public string? DisplayName { get; init; }
    public string? PermissionMode { get; init; }

    public JoinCode.Abstractions.LLM.Chat.CacheSafeParams? CacheSafeParams { get; set; }

    /// <summary>
    /// 内容替换状态 — 对齐 TS ToolUseContext.contentReplacementState
    /// 子智能体默认克隆父级状态（缓存共享 fork 需要相同决策）
    /// </summary>
    public JoinCode.Abstractions.LLM.Chat.ContentReplacementState? ContentReplacementState { get; set; }

    public string? ConsumeInvokingRequestId()
    {
        var id = InvokingRequestId;
        InvokingRequestId = null;
        return id;
    }

    public IDisposable EnterScope() => AsyncLocalScope<SubAgentContext?>.Enter(_current, this);

    public IDisposable EnterScopeWithCwd(string? cwd)
    {
        CwdOverride = cwd;
        return AsyncLocalScope<SubAgentContext?>.Enter(_current, this);
    }

    public static string GetEffectiveCwd(string? fallbackCwd = null)
    {
        return _current.Value?.CwdOverride ?? fallbackCwd ?? Environment.CurrentDirectory;
    }
}
