namespace JoinCode.Abstractions.Insights;

/// <summary>
/// Facet 聚合摘要 — 对齐 TS insights.ts facets_summary
/// </summary>
public sealed class FacetSummary {
    /// <summary>获取总会话数。</summary>
    public int Total { get; init; }
    /// <summary>获取目标分类计数字典。</summary>
    public IReadOnlyDictionary<string, int> GoalCategories { get; init; } = new Dictionary<string, int>();
    /// <summary>获取结果分类计数字典。</summary>
    public IReadOnlyDictionary<string, int> Outcomes { get; init; } = new Dictionary<string, int>();
    /// <summary>获取满意度计数字典。</summary>
    public IReadOnlyDictionary<string, int> Satisfaction { get; init; } = new Dictionary<string, int>();
    /// <summary>获取摩擦点计数字典。</summary>
    public IReadOnlyDictionary<string, int> Friction { get; init; } = new Dictionary<string, int>();
    /// <summary>获取有用性计数字典。</summary>
    public IReadOnlyDictionary<string, int> Helpfulness { get; init; } = new Dictionary<string, int>();
    /// <summary>获取会话类型计数字典。</summary>
    public IReadOnlyDictionary<string, int> SessionTypes { get; init; } = new Dictionary<string, int>();
    /// <summary>获取主要成功点计数字典。</summary>
    public IReadOnlyDictionary<string, int> PrimarySuccesses { get; init; } = new Dictionary<string, int>();

    /// <summary>所有 brief_summary 列表（用于 Insight 生成上下文）</summary>
    public IReadOnlyList<string> BriefSummaries { get; init; } = Array.Empty<string>();

    /// <summary>所有 friction_detail 列表</summary>
    public IReadOnlyList<string> FrictionDetails { get; init; } = Array.Empty<string>();

    /// <summary>所有用户指令（去重后）</summary>
    public IReadOnlyList<string> UserInstructions { get; init; } = Array.Empty<string>();
}