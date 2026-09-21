namespace JoinCode.Abstractions.Interfaces;

public sealed class SubAgentContext {
    private static readonly AsyncLocal<SubAgentContext?> _current = new();

    /// <summary>获取当前异步上下文中的 SubAgentContext。</summary>
    public static SubAgentContext? Current => _current.Value;

    /// <summary>获取 Agent 标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取 Agent 角色。</summary>
    public required AgentRole Role { get; init; }
    /// <summary>获取或设置执行器变体。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>获取任务描述。</summary>
    public required string Task { get; init; }
    /// <summary>获取或设置父 Agent 标识。</summary>
    public string? ParentAgentId { get; set; }
    /// <summary>获取或设置会话标识。</summary>
    public string? SessionId { get; set; }
    /// <summary>获取或设置工作树路径。</summary>
    public string? WorktreePath { get; set; }
    /// <summary>获取或设置当前工作目录覆盖值。</summary>
    public string? CwdOverride { get; set; }
    /// <summary>获取或设置团队标识。</summary>
    public string? TeamId { get; set; }
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取或设置开始时间。</summary>
    public DateTime? StartedAt { get; set; }
    /// <summary>获取或设置完成时间。</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>获取或设置工具调用次数。</summary>
    public int ToolCallCount { get; set; }
    /// <summary>获取 Token 使用情况。</summary>
    public TokenUsage TokenUsage { get; } = new();
    /// <summary>获取或设置 Agent 状态。</summary>
    public AgentStatus Status { get; set; } = AgentStatus.Pending;

    /// <summary>获取允许使用的工具列表。</summary>
    public IEnumerable<string> AllowedTools { get; init; } = [];
    /// <summary>获取禁止使用的工具列表。</summary>
    public IEnumerable<string> DeniedTools { get; init; } = [];

    /// <summary>获取或设置子智能体名称。</summary>
    public string? SubagentName { get; init; }
    /// <summary>获取是否为内置子智能体。</summary>
    public bool IsBuiltIn { get; init; }
    /// <summary>获取或设置发起调用的请求标识。</summary>
    public string? InvokingRequestId { get; set; }
    /// <summary>获取或设置是否已发出调用事件。</summary>
    public bool InvocationEmitted { get; set; }
    /// <summary>获取或设置显示名称。</summary>
    public string? DisplayName { get; init; }
    /// <summary>获取或设置权限模式。</summary>
    public string? PermissionMode { get; init; }

    /// <summary>获取或设置缓存安全参数。</summary>
    public JoinCode.Abstractions.LLM.Chat.CacheSafeParams? CacheSafeParams { get; set; }

    /// <summary>
    /// 内容替换状态 — 对齐 TS ToolUseContext.contentReplacementState
    /// 子智能体默认克隆父级状态（缓存共享 fork 需要相同决策）
    /// </summary>
    public JoinCode.Abstractions.LLM.Chat.ContentReplacementState? ContentReplacementState { get; set; }

    /// <summary>Teammate 元信息 — 非 null 表示当前上下文为 teammate 执行</summary>
    public TeammateMeta? TeammateMeta { get; set; }

    /// <summary>消费并清空发起调用的请求标识,返回原值。</summary>
    public string? ConsumeInvokingRequestId() {
        var id = InvokingRequestId;
        InvokingRequestId = null;
        return id;
    }

    /// <summary>进入当前上下文作用域。</summary>
    public IDisposable EnterScope() => AsyncLocalScope<SubAgentContext?>.Enter(_current, this);

    /// <summary>设置工作目录覆盖值并进入上下文作用域。</summary>
    public IDisposable EnterScopeWithCwd(string? cwd) {
        CwdOverride = cwd;
        return AsyncLocalScope<SubAgentContext?>.Enter(_current, this);
    }

    /// <summary>获取有效工作目录,优先使用上下文覆盖值,其次回退参数,最后使用环境当前目录。</summary>
    public static string GetEffectiveCwd(string? fallbackCwd = null) {
        return _current.Value?.CwdOverride ?? fallbackCwd ?? Environment.CurrentDirectory;
    }

    /// <summary>投影为 AgentCoreIdentity</summary>
    public AgentCoreIdentity ToIdentity() => new(AgentId, DisplayName, Role, Variant);
}
