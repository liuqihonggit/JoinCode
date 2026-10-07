namespace Tools.Handlers;

/// <summary>
/// 创建子代理的选项参数
/// </summary>
public sealed record AgentCreateOptions {
    /// <summary>代理描述（3-5 个词）</summary>
    [McpToolParameter("Agent description (3-5 words)")]
    public required string Description { get; init; }

    /// <summary>任务提示词/指令</summary>
    [McpToolParameter("Task prompt/instructions")]
    public required string Prompt { get; init; }

    /// <summary>代理类型（可选）</summary>
    [McpToolParameter("Agent type (optional)", Required = false)]
    public string? SubagentType { get; init; }

    /// <summary>模型覆盖: sonnet/opus/haiku（可选）</summary>
    [McpToolParameter("Model override: sonnet/opus/haiku (optional)", Required = false)]
    public string? Model { get; init; }

    /// <summary>代理名称，用于 SendMessage 寻址（可选）</summary>
    [McpToolParameter("Agent name for SendMessage addressing (optional)", Required = false)]
    public string? Name { get; init; }

    /// <summary>是否在后台运行（可选，默认 false）</summary>
    [McpToolParameter("Run in background", Required = false)]
    public bool? RunInBackground { get; init; } = false;

    /// <summary>隔离模式: none/worktree（可选，默认 none）</summary>
    [McpToolParameter("Isolation mode: none/worktree (optional)", Required = false)]
    public string? Isolation { get; init; } = "none";

    /// <summary>工作目录覆盖（可选）</summary>
    [McpToolParameter("Working directory override (optional)", Required = false)]
    public string? Cwd { get; init; }

    /// <summary>记忆作用域: user/project/local（可选，启用代理记忆）</summary>
    [McpToolParameter("Memory scope: user/project/local (optional, enables agent memory)", Required = false)]
    public string? Memory { get; init; }

    /// <summary>
    /// 干跑模式 — 不调用 LLM，直接创建 mock agent 并持久化到文件，支持跨进程测试完整链路
    /// </summary>
    [McpToolParameter("Dry run mode: skip LLM, create mock agent for testing (optional)", Required = false)]
    public bool? DryRun { get; init; }
}

/// <summary>
/// Agent 工具处理器 - 创建和管理子代理
/// 通过中间件管道处理验证、fork判断、spawn、流式执行、handoff审查
/// 拆分后职责：字段+构造（共享依赖）。
/// 创建+dry-run → <see cref="AgentToolHandlers"/>.Create.cs（partial）。
/// 查询+状态 → <see cref="AgentToolHandlers"/>.Query.cs（partial）。
/// 消息通信 → <see cref="AgentToolHandlers"/>.Messaging.cs（partial）。
/// 诊断消息 → <see cref="AgentDiagnostics"/>。
/// </summary>
[McpToolDispatch(ToolCategory.Agent, Optional = true)]
public partial class AgentToolHandlers {
    private readonly MiddlewarePipeline<AgentToolContext> _pipeline;
    private readonly IAgentService _agentService;
    private readonly IAgentService? _coordinator;
    private readonly ILogger<AgentToolHandlers>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ITelemetryService? _telemetryService;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ITeamManager? _teamManager;
    private readonly IClockService _clock;
    private readonly IWorktreeDecisionPolicy? _worktreeDecisionPolicy;
    private readonly IAgentWorktreeManager? _worktreeManager;

    /// <summary>
    /// 构造 Agent 工具处理器
    /// </summary>
    public AgentToolHandlers(
        MiddlewarePipeline<AgentToolContext> pipeline,
        IAgentService agentService,
        IAgentService? coordinator = null,
        ILogger<AgentToolHandlers>? logger = null,
        ITelemetryService? telemetryService = null,
        IServiceProvider? serviceProvider = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null,
        ITeamManager? teamManager = null,
        IWorktreeDecisionPolicy? worktreeDecisionPolicy = null,
        IAgentWorktreeManager? worktreeManager = null) {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _coordinator = coordinator;
        _logger = logger;
        _telemetryService = telemetryService;
        _serviceProvider = serviceProvider;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
        _teamManager = teamManager;
        _worktreeDecisionPolicy = worktreeDecisionPolicy;
        _worktreeManager = worktreeManager;
    }
}

/// <summary>
/// Dry-run agent 持久化状态 — 跨进程共享 mock agent 状态
/// </summary>
public sealed class DryRunAgentState {
    /// <summary>代理ID</summary>
    public required string Id { get; set; }
    /// <summary>代理描述</summary>
    public required string Description { get; set; }
    /// <summary>代理状态（running/stopped/completed）</summary>
    public required string Status { get; set; }
    /// <summary>启动时间</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>完成时间（未完成时为 null）</summary>
    public DateTime? CompletedAt { get; set; }
    /// <summary>任务提示词</summary>
    public string? Prompt { get; set; }
    /// <summary>隔离模式（none/worktree）— dry_run 也走决策链路</summary>
    public string? IsolationMode { get; set; }
    /// <summary>Worktree 路径 — 隔离模式为 worktree 时填充</summary>
    public string? WorktreePath { get; set; }
    /// <summary>Worktree 分支名 — 隔离模式为 worktree 时填充</summary>
    public string? WorktreeBranch { get; set; }
}

/// <summary>
/// Dry-run agent 消息 — 跨进程共享 mock agent 消息
/// </summary>
public sealed class DryRunAgentMessage {
    /// <summary>消息内容</summary>
    public required string Content { get; set; }
    /// <summary>消息摘要预览</summary>
    public string? Summary { get; set; }
    /// <summary>消息时间戳</summary>
    public DateTime Timestamp { get; set; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true)]
[JsonSerializable(typeof(DryRunAgentState))]
[JsonSerializable(typeof(List<DryRunAgentMessage>))]
internal sealed partial class DryRunAgentStateJsonContext : JsonSerializerContext;
