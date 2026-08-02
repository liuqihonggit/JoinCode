
namespace Core.Agents;

/// <summary>
/// 内置 Agent 接口 - 所有内置 Agent 的基础接口
/// 独立于 IAgent，内置Agent是轻量级的对话处理器，不需要Execute/Pause/Resume/Cancel
/// </summary>
public interface IBuiltInAgent : IAsyncDisposable
{
    string Name { get; }
    string Description { get; }
    BuiltInAgentType AgentType { get; }
    string SystemPrompt { get; }

    Task<AgentResponse> ProcessAsync(
        string userInput,
        bool useTools = false,
        CancellationToken cancellationToken = default);

    Task ClearContextAsync(CancellationToken cancellationToken = default);
    Task<AgentContext> GetContextAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 内置 Agent 类型
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 BuiltInAgentTypeConstants + BuiltInAgentTypeExtensions
/// </summary>
public enum BuiltInAgentType
{
    /// <summary>
    /// 计划 Agent - 制定任务执行计划
    /// </summary>
    [EnumValue("plan")] Plan,

    /// <summary>
    /// 探索 Agent - 探索代码库结构
    /// </summary>
    [EnumValue("explore")] Explore,

    /// <summary>
    /// 验证 Agent - 验证代码正确性
    /// </summary>
    [EnumValue("verification")] Verification,

    /// <summary>
    /// 通用 Agent - 处理通用任务
    /// </summary>
    [EnumValue("generalPurpose")] GeneralPurpose,

    /// <summary>
    /// Claude Code 引导 Agent - 提供使用指导
    /// </summary>
    [EnumValue("claudeCodeGuide")] ClaudeCodeGuide,

    /// <summary>
    /// 上下文压缩 Agent - 智能压缩和管理上下文
    /// </summary>
    [EnumValue("contextCompression")] ContextCompression
}

/// <summary>
/// 内置 Agent 配置
/// </summary>
public sealed record BuiltInAgentConfig
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required BuiltInAgentType AgentType { get; init; }
    public required string SystemPrompt { get; init; }
    public float Temperature { get; init; } = 0.7f;
    public int MaxTokens { get; init; } = 4000;
}

/// <summary>
/// Agent 执行选项
/// </summary>
public sealed record AgentExecutionOptions
{
    public string? Goal { get; init; }
    public string? Context { get; init; }
    public IReadOnlyList<ToolDefinition>? AvailableTools { get; init; }
    public float Temperature { get; init; } = 0.7f;
    public int MaxTokens { get; init; } = 4000;
}
