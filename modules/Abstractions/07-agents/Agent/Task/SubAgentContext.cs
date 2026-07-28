namespace JoinCode.Abstractions.Interfaces;

public sealed class SubAgentContext
{
    private static readonly AsyncLocal<SubAgentContext?> _current = new();
    private static readonly AsyncLocal<string?> _cwdOverride = new();

    public static SubAgentContext? Current => _current.Value;

    public static string? CwdOverride
    {
        get => _cwdOverride.Value;
        set => _cwdOverride.Value = value;
    }

    public required string AgentId { get; init; }
    public required string AgentType { get; init; }
    public required string Task { get; init; }
    public string? ParentAgentId { get; set; }
    public string? SessionId { get; set; }
    public string? WorktreePath { get; set; }
    public string? TeamId { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public int ToolCallCount { get; set; }
    public TokenUsage TokenUsage { get; } = new();
    public AgentStatus Status { get; set; } = AgentStatus.Pending;

    public IReadOnlyList<string>? AllowedTools { get; init; }
    public IReadOnlyList<string>? DeniedTools { get; init; }

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

    public IDisposable EnterScope()
    {
        var previous = _current.Value;
        _current.Value = this;
        return new ScopeRestore(previous);
    }

    public IDisposable EnterScopeWithCwd(string? cwd)
    {
        var previousContext = _current.Value;
        var previousCwd = _cwdOverride.Value;
        _current.Value = this;
        _cwdOverride.Value = cwd;
        return new DualScopeRestore(previousContext, previousCwd);
    }

    public static string GetEffectiveCwd(string? fallbackCwd = null)
    {
        return _cwdOverride.Value ?? fallbackCwd ?? Environment.CurrentDirectory;
    }

    private sealed class ScopeRestore(SubAgentContext? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }

    private sealed class DualScopeRestore(SubAgentContext? previousContext, string? previousCwd) : IDisposable
    {
        public void Dispose()
        {
            _current.Value = previousContext;
            _cwdOverride.Value = previousCwd;
        }
    }
}
