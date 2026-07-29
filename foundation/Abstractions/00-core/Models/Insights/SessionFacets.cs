namespace JoinCode.Abstractions.Insights;

/// <summary>
/// 会话 Facet 数据 — 对齐 TS insights.ts SessionFacets
/// 由 LLM 从会话转录中提取的结构化洞察
/// </summary>
public sealed class SessionFacets
{
    public string SessionId { get; init; } = string.Empty;

    /// <summary>用户根本想达成什么</summary>
    public string UnderlyingGoal { get; init; } = string.Empty;

    /// <summary>目标类别计数 — 对齐 TS goal_categories</summary>
    public IReadOnlyDictionary<string, int> GoalCategories { get; init; } = new Dictionary<string, int>();

    /// <summary>结果: fully_achieved | mostly_achieved | partially_achieved | not_achieved | unclear_from_transcript</summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>用户满意度计数 — 对齐 TS user_satisfaction_counts</summary>
    public IReadOnlyDictionary<string, int> UserSatisfactionCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>Claude 帮助度: unhelpful | slightly_helpful | moderately_helpful | very_helpful | essential</summary>
    public string ClaudeHelpfulness { get; init; } = string.Empty;

    /// <summary>会话类型: single_task | multi_task | iterative_refinement | exploration | quick_question</summary>
    public string SessionType { get; init; } = string.Empty;

    /// <summary>摩擦类型计数 — 对齐 TS friction_counts</summary>
    public IReadOnlyDictionary<string, int> FrictionCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>一句话描述摩擦</summary>
    public string FrictionDetail { get; init; } = string.Empty;

    /// <summary>主要成功因素: none | fast_accurate_search | correct_code_edits | good_explanations | proactive_help | multi_file_changes | good_debugging</summary>
    public string PrimarySuccess { get; init; } = string.Empty;

    /// <summary>一句话：用户想要什么，是否得到</summary>
    public string BriefSummary { get; init; } = string.Empty;

    /// <summary>用户给 Claude 的指令</summary>
    public IReadOnlyList<string> UserInstructionsToClaude { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Facet 提取结果 — 包含提取的 facets 和缓存状态
/// </summary>
public sealed class FacetExtractionResult
{
    public required SessionFacets Facets { get; init; }
    public bool FromCache { get; init; }
}

/// <summary>
/// Multi-Clauding 检测结果 — 对齐 TS detectMultiClauding
/// </summary>
public sealed class MultiClaudingResult
{
    /// <summary>交替会话对数</summary>
    public int OverlapEvents { get; init; }

    /// <summary>涉及的会话数</summary>
    public int SessionsInvolved { get; init; }

    /// <summary>交替期间的用户消息数</summary>
    public int UserMessagesDuring { get; init; }
}

[JsonSerializable(typeof(SessionFacets))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(List<string>))]
public sealed partial class SessionFacetsJsonContext : JsonSerializerContext;
