namespace Core.Agents;

/// <summary>
/// 子智能体初始化管道上下文 — 贯穿所有 Spawn 中间件
/// </summary>
public sealed class AgentSpawnContext
{
    /// <summary>
    /// 原始 Spawn 选项
    /// </summary>
    public required AgentSpawnOptions Options { get; init; }

    /// <summary>
    /// 解析后的 Agent 定义（DefinitionResolutionMiddleware 设置）
    /// </summary>
    public JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? Definition { get; set; }

    /// <summary>
    /// 构建后的系统提示词（PromptBuildingMiddleware 设置）
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 进度追踪器
    /// </summary>
    public ProgressTracker ProgressTracker { get; } = new();

    /// <summary>
    /// 过滤后的 CacheSafeParams（ContextSetupMiddleware 设置）
    /// </summary>
    public JoinCode.Abstractions.LLM.Chat.CacheSafeParams? CacheSafeParams { get; set; }

    /// <summary>
    /// 构建后的 SubAgentOptions（ContextSetupMiddleware 设置）
    /// </summary>
    public SubAgentOptions? SubOptions { get; set; }

    /// <summary>
    /// Spawn 后的子智能体实例（SpawnMiddleware 设置）
    /// </summary>
    public ISubAgent? SubAgent { get; set; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }
}
