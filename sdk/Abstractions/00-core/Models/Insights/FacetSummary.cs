namespace JoinCode.Abstractions.Insights;

/// <summary>
/// Facet 聚合摘要 — 对齐 TS insights.ts facets_summary
/// </summary>
public sealed class FacetSummary
{
    public int Total { get; init; }
    public IReadOnlyDictionary<string, int> GoalCategories { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> Outcomes { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> Satisfaction { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> Friction { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> Helpfulness { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> SessionTypes { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> PrimarySuccesses { get; init; } = new Dictionary<string, int>();

    /// <summary>所有 brief_summary 列表（用于 Insight 生成上下文）</summary>
    public IReadOnlyList<string> BriefSummaries { get; init; } = Array.Empty<string>();

    /// <summary>所有 friction_detail 列表</summary>
    public IReadOnlyList<string> FrictionDetails { get; init; } = Array.Empty<string>();

    /// <summary>所有用户指令（去重后）</summary>
    public IReadOnlyList<string> UserInstructions { get; init; } = Array.Empty<string>();
}
