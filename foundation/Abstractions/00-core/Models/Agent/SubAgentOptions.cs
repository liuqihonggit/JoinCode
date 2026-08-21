namespace JoinCode.Abstractions.Models.Agent;

public sealed class SubAgentOptions
{
    public AgentRole Role { get; init; } = AgentRole.Executor;
    public ExecutorVariant? Variant { get; init; }
    public string? AdditionalInstructions { get; init; }
    public int MaxIterations { get; init; } = 50;
    public bool EnableThinking { get; init; } = false;
    public string? ModelName { get; init; }
    public float Temperature { get; init; } = 0.7f;
    public string? DisplayName { get; init; }
    public string? ColorHex { get; init; }
    public string? SpinnerVerb { get; init; }
    public string? SystemPrompt { get; init; }
    public List<string>? AllowedTools { get; init; }
    public List<string>? DeniedTools { get; init; }
    public MessageList? InitialMessageList { get; init; }
    public List<string>? PreloadSkills { get; init; }
    /// <summary>首轮前置 prompt — spawn 时作为第一条 user message 注入,支持斜杠命令</summary>
    public string? InitialPrompt { get; init; }
    /// <summary>
    /// 每轮重注入的关键系统提醒 — 对齐 claude code criticalSystemReminder_EXPERIMENTAL
    /// <para>每轮 ExecuteAsync 时作为 user message 注入到消息流,保持紧迫感(如 "CRITICAL: 这是验证任务,不要改代码")</para>
    /// </summary>
    public string? CriticalSystemReminder { get; init; }
    public string? PermissionMode { get; init; }
    public string? WorktreePath { get; set; }
    public string? WorktreeBranch { get; set; }
    public string? SubagentName { get; init; }
    public bool IsBuiltIn { get; init; }
    public JoinCode.Abstractions.LLM.Chat.CacheSafeParams? CacheSafeParams { get; init; }
    public JoinCode.Abstractions.Interfaces.IProgressTracker? ProgressTracker { get; init; }
    public JoinCode.Abstractions.LLM.Chat.ContentReplacementState? ContentReplacementState { get; init; }
    public string? SessionId { get; init; }
    public JoinCode.Abstractions.Interfaces.IFileStateCache? ReadFileState { get; init; }
    public string? Effort { get; init; }
    public string? GoalId { get; init; }
    public string? GraphNodeId { get; init; }
    public int? TokenBudget { get; init; }
    public bool FreshContext { get; init; }
}
