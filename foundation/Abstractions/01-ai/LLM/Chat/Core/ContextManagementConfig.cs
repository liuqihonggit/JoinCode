namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// API 端上下文管理配置 — 对齐 TS apiMicrocompact.ts ContextManagementConfig
/// 通过 context_management 请求参数告诉 Anthropic API 在服务端自动清理工具结果
/// </summary>
public sealed class ContextManagementConfig
{
    /// <summary>
    /// 上下文编辑策略列表
    /// </summary>
    public required IReadOnlyList<ContextEditStrategy> Edits { get; init; }
}

/// <summary>
/// 上下文编辑策略 — 对齐 TS ContextEditStrategy
/// 联合类型: ClearToolUses 或 ClearThinking
/// </summary>
public abstract class ContextEditStrategy
{
    /// <summary>
    /// 策略类型标识
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// 清除工具使用记录策略 — 对齐 TS clear_tool_uses_20250919
/// 让 API 在服务端自动清除旧工具结果，不破坏 prompt cache
/// </summary>
public sealed class ClearToolUsesStrategy : ContextEditStrategy
{
    public override string Type => "clear_tool_uses_20250919";

    /// <summary>
    /// 触发条件 — 输入 token 阈值
    /// </summary>
    public ContextTrigger? Trigger { get; init; }

    /// <summary>
    /// 保留最近 N 个工具使用记录
    /// </summary>
    public ContextKeep? Keep { get; init; }

    /// <summary>
    /// 清除哪些工具的输入内容 — true=全部, string[]=指定工具名
    /// </summary>
    public object? ClearToolInputs { get; init; }

    /// <summary>
    /// 排除的工具名列表 — 这些工具的使用记录不会被清除
    /// </summary>
    public IReadOnlyList<string>? ExcludeTools { get; init; }

    /// <summary>
    /// 至少清除的 token 数
    /// </summary>
    public ContextTokenThreshold? ClearAtLeast { get; init; }
}

/// <summary>
/// 清除 thinking 块策略 — 对齐 TS clear_thinking_20251015
/// 让 API 在服务端自动清除旧的 thinking 块
/// </summary>
public sealed class ClearThinkingStrategy : ContextEditStrategy
{
    public override string Type => "clear_thinking_20251015";

    /// <summary>
    /// 保留策略 — "all" 保留全部, 或指定保留最近 N 个 thinking turn
    /// </summary>
    public required object Keep { get; init; }
}

/// <summary>
/// 触发条件 — 输入 token 阈值
/// </summary>
public sealed class ContextTrigger
{
    public required string Type { get; init; }
    public required int Value { get; init; }
}

/// <summary>
/// 保留策略 — 工具使用记录数
/// </summary>
public sealed class ContextKeep
{
    public required string Type { get; init; }
    public required int Value { get; init; }
}

/// <summary>
/// Token 阈值 — 至少清除的 token 数
/// </summary>
public sealed class ContextTokenThreshold
{
    public required string Type { get; init; }
    public required int Value { get; init; }
}
