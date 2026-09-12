namespace JoinCode.Abstractions.Insights;

/// <summary>
/// 会话洞察元数据 — 对齐 TS insights.ts SessionMeta
/// 注意: 与 LLM/Chat/Session/SessionMeta 不同，后者用于上下文压缩缓存
/// </summary>
public sealed class InsightSessionMeta
{
    public string SessionId { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public double DurationMinutes { get; init; }

    /// <summary>用户消息数（仅人类消息，不含 tool_result）</summary>
    public int UserMessageCount { get; init; }

    /// <summary>助手消息数</summary>
    public int AssistantMessageCount { get; init; }

    /// <summary>输入 Token 数</summary>
    public long InputTokens { get; init; }

    /// <summary>输出 Token 数</summary>
    public long OutputTokens { get; init; }

    /// <summary>工具使用统计 (工具名 → 使用次数)</summary>
    public IReadOnlyDictionary<string, int> ToolCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>语言分布 (语言名 → 出现次数)</summary>
    public IReadOnlyDictionary<string, int> Languages { get; init; } = new Dictionary<string, int>();

    /// <summary>Git 提交数</summary>
    public int GitCommits { get; init; }

    /// <summary>Git 推送数</summary>
    public int GitPushes { get; init; }

    /// <summary>行添加数</summary>
    public int LinesAdded { get; init; }

    /// <summary>行删除数</summary>
    public int LinesRemoved { get; init; }

    /// <summary>文件修改数</summary>
    public int FilesModified { get; init; }

    /// <summary>用户中断次数</summary>
    public int UserInterruptions { get; init; }

    /// <summary>工具错误数</summary>
    public int ToolErrors { get; init; }

    /// <summary>工具错误分类 (类别 → 次数)</summary>
    public IReadOnlyDictionary<string, int> ToolErrorCategories { get; init; } = new Dictionary<string, int>();

    /// <summary>是否使用 TaskAgent</summary>
    public bool UsesTaskAgent { get; init; }

    /// <summary>是否使用 MCP</summary>
    public bool UsesMcp { get; init; }

    /// <summary>是否使用 WebSearch</summary>
    public bool UsesWebSearch { get; init; }

    /// <summary>是否使用 WebFetch</summary>
    public bool UsesWebFetch { get; init; }

    /// <summary>首次提示内容</summary>
    public string FirstPrompt { get; init; } = string.Empty;

    /// <summary>会话摘要</summary>
    public string? Summary { get; init; }

    /// <summary>估算成本（美元）</summary>
    public decimal EstimatedCostUsd { get; init; }

    /// <summary>用户消息时间戳列表 — 用于 Multi-Clauding 检测</summary>
    public IReadOnlyList<DateTime> UserMessageTimestamps { get; init; } = Array.Empty<DateTime>();

    /// <summary>关联的 Facet 数据（如果已提取）</summary>
    public SessionFacets? Facets { get; init; }
}
