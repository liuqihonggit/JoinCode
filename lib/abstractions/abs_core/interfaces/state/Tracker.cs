namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Token 使用记录
/// </summary>
public sealed record TokenUsageRecord
{
    /// <summary>
    /// 记录时间戳
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 模型名称
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Prompt Token 数
    /// </summary>
    public int PromptTokens { get; init; }

    /// <summary>
    /// Completion Token 数
    /// </summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// 缓存创建 Token 数（Anthropic Prompt Caching）
    /// </summary>
    public int CacheCreationTokens { get; init; }

    /// <summary>
    /// 缓存读取 Token 数（Anthropic Prompt Caching）
    /// </summary>
    public int CacheReadTokens { get; init; }

    /// <summary>
    /// 成本（美元）
    /// </summary>
    public decimal CostUsd { get; init; }

    /// <summary>
    /// 会话 ID
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// API 调用持续时间（毫秒）
    /// </summary>
    public double ApiDurationMs { get; init; }
}

/// <summary>
/// 成本追踪器接口
/// </summary>
public interface ICostTracker
{
    /// <summary>
    /// 记录 Token 使用
    /// </summary>
    void RecordUsage(string model, int promptTokens, int completionTokens, string? sessionId = null);

    /// <summary>
    /// 记录 Token 使用（含缓存 Token）
    /// </summary>
    void RecordUsage(string model, int promptTokens, int completionTokens, int cacheCreationTokens, int cacheReadTokens, string? sessionId = null);

    /// <summary>
    /// 记录 Token 使用（含缓存 Token 和 API 持续时间）
    /// </summary>
    void RecordUsage(string model, int promptTokens, int completionTokens, int cacheCreationTokens, int cacheReadTokens, double apiDurationMs, string? sessionId = null);

    /// <summary>
    /// 记录代码变更行数
    /// </summary>
    void RecordLinesChanged(int added, int removed);

    /// <summary>
    /// 获取会话统计
    /// </summary>
    CostStatistics GetSessionStatistics(string sessionId);

    /// <summary>
    /// 获取今日统计
    /// </summary>
    CostStatistics GetTodayStatistics();

    /// <summary>
    /// 获取总统计
    /// </summary>
    CostStatistics GetTotalStatistics();

    /// <summary>
    /// 重置所有用量记录 — 对齐 TS login.tsx resetCostState
    /// </summary>
    void Reset();
}

/// <summary>
/// 成本统计数据
/// </summary>
public sealed class CostStatistics
{
    public int RequestCount { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
    public int CacheCreationTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public decimal CacheSavingsUsd { get; init; }
    public decimal TotalCostUsd { get; init; }
    public List<ModelCostStatistics> ModelBreakdown { get; init; } = new();
    public TimeSpan ApiDuration { get; init; }
    public TimeSpan WallDuration { get; init; }
    public int LinesAdded { get; init; }
    public int LinesRemoved { get; init; }
    public bool HasUnknownModelCost { get; init; }
}

/// <summary>
/// 模型成本统计
/// </summary>
public sealed class ModelCostStatistics
{
    public required string Model { get; init; }
    public int RequestCount { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
    public int CacheCreationTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public decimal TotalCost { get; init; }
}
