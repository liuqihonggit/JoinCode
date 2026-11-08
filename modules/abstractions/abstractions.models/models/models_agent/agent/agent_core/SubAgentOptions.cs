namespace JoinCode.Abstractions.Models.Agent;

public sealed class SubAgentOptions {
    /// <summary>获取子智能体角色。</summary>
    public AgentRole Role { get; init; } = AgentRole.Executor;
    /// <summary>获取执行器变体。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>获取附加指令。</summary>
    public string? AdditionalInstructions { get; init; }
    /// <summary>获取最大迭代次数。</summary>
    public int MaxIterations { get; init; } = 50;
    /// <summary>获取是否启用思考模式。</summary>
    public bool EnableThinking { get; init; } = false;
    /// <summary>获取模型名称。</summary>
    public string? ModelName { get; init; }
    /// <summary>获取采样温度。</summary>
    public float Temperature { get; init; } = 0.7f;
    /// <summary>获取显示名称。</summary>
    public string? DisplayName { get; init; }
    /// <summary>获取颜色十六进制值。</summary>
    public string? ColorHex { get; init; }
    /// <summary>获取旋转动词文本。</summary>
    public string? SpinnerVerb { get; init; }
    /// <summary>获取系统提示词。</summary>
    public string? SystemPrompt { get; init; }
    /// <summary>获取允许的工具列表。</summary>
    public List<string> AllowedTools { get; init; } = [];
    /// <summary>获取禁用的工具列表。</summary>
    public List<string> DeniedTools { get; init; } = [];
    /// <summary>获取初始消息列表。</summary>
    public MessageList? InitialMessageList { get; init; }
    /// <summary>获取预加载技能列表。</summary>
    public List<string> PreloadSkills { get; init; } = [];
    /// <summary>首轮前置 prompt — spawn 时作为第一条 user message 注入,支持斜杠命令</summary>
    public string? InitialPrompt { get; init; }
    /// <summary>
    /// 每轮重注入的关键系统提醒 — 对齐 TS 原版 criticalSystemReminder_EXPERIMENTAL
    /// <para>每轮 ExecuteAsync 时作为 user message 注入到消息流,保持紧迫感(如 "CRITICAL: 这是验证任务,不要改代码")</para>
    /// </summary>
    public string? CriticalSystemReminder { get; init; }
    /// <summary>获取权限模式。</summary>
    public string? PermissionMode { get; init; }
    /// <summary>获取或设置工作树路径。</summary>
    public string? WorktreePath { get; set; }
    /// <summary>获取或设置工作树分支。</summary>
    public string? WorktreeBranch { get; set; }
    /// <summary>获取子智能体名称。</summary>
    public string? SubagentName { get; init; }
    /// <summary>获取是否为内置子智能体。</summary>
    public bool IsBuiltIn { get; init; }
    /// <summary>获取缓存安全参数。</summary>
    public JoinCode.Abstractions.LLM.Chat.CacheSafeParams? CacheSafeParams { get; init; }
    /// <summary>获取进度追踪器。</summary>
    public JoinCode.Abstractions.Interfaces.IProgressTracker? ProgressTracker { get; init; }
    /// <summary>获取内容替换状态。</summary>
    public JoinCode.Abstractions.LLM.Chat.ContentReplacementState? ContentReplacementState { get; init; }
    /// <summary>获取会话标识。</summary>
    public string? SessionId { get; init; }
    /// <summary>获取文件状态缓存。</summary>
    public JoinCode.Abstractions.Interfaces.IFileStateCache? ReadFileState { get; init; }
    /// <summary>获取努力级别。</summary>
    public string? Effort { get; init; }
    /// <summary>获取目标标识。</summary>
    public string? GoalId { get; init; }
    /// <summary>获取图节点标识。</summary>
    public string? GraphNodeId { get; init; }
    /// <summary>获取 Token 预算。</summary>
    public int? TokenBudget { get; init; }
    /// <summary>获取是否使用全新上下文。</summary>
    public bool FreshContext { get; init; }
}
