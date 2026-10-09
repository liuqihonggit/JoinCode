namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具转移角色 — 三角色模型：primary（首选）/ fallback（备选）/ refine（精炼）
/// </summary>
public enum ToolTransitionRole {
    /// <summary>首选工具 — 正常路径优先使用。</summary>
    Primary = 0,
    /// <summary>备选工具 — primary 失败或不可用时回退。</summary>
    Fallback = 1,
    /// <summary>精炼工具 — 数据太大/太杂时精炼结果（如 gh run view 拿到大日志 → filter=error）。</summary>
    Refine = 2,
}

/// <summary>
/// 工具转移条件 — 描述从当前工具转移到下一个工具的条件
/// </summary>
public sealed record ToolTransitionCondition {
    /// <summary>获取条件标识（如 "data_too_large"、"primary_failed"、"normal"）。</summary>
    public required string Id { get; init; }
    /// <summary>获取条件描述。</summary>
    public string? Description { get; init; }
    /// <summary>获取首选工具名。</summary>
    public string? Primary { get; init; }
    /// <summary>获取备选工具名。</summary>
    public string? Fallback { get; init; }
    /// <summary>获取精炼工具名。</summary>
    public string? Refine { get; init; }
}

/// <summary>
/// 工具转移模型 — 按转移条件分组，每组三角色 primary/fallback/refine
/// 运行时学习用户工具使用习惯，驱动链路推荐
/// </summary>
public sealed class ToolTransitionModel {
    private readonly Dictionary<string, ToolTransitionCondition> _conditions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>获取所有转移条件。</summary>
    public IReadOnlyDictionary<string, ToolTransitionCondition> Conditions => _conditions;

    /// <summary>添加或更新转移条件。</summary>
    public void AddOrUpdate(ToolTransitionCondition condition) {
        _conditions[condition.Id] = condition;
    }

    /// <summary>按角色获取推荐工具名。</summary>
    public string? GetRecommendation(string conditionId, ToolTransitionRole role) {
        if (!_conditions.TryGetValue(conditionId, out var condition)) return null;
        return role switch {
            ToolTransitionRole.Primary => condition.Primary,
            ToolTransitionRole.Fallback => condition.Fallback,
            ToolTransitionRole.Refine => condition.Refine,
            _ => null,
        };
    }

    /// <summary>获取所有角色推荐（非 null 的角色）。</summary>
    public IReadOnlyList<(ToolTransitionRole Role, string ToolName)> GetAllRecommendations(string conditionId) {
        if (!_conditions.TryGetValue(conditionId, out var condition)) return [];
        var result = new List<(ToolTransitionRole, string)>(3);
        if (condition.Primary is not null) result.Add((ToolTransitionRole.Primary, condition.Primary));
        if (condition.Fallback is not null) result.Add((ToolTransitionRole.Fallback, condition.Fallback));
        if (condition.Refine is not null) result.Add((ToolTransitionRole.Refine, condition.Refine));
        return result;
    }
}

/// <summary>
/// 精炼推荐规则 — 数据过大/过杂时推荐精炼工具
/// </summary>
public sealed record ToolRefineRule {
    /// <summary>获取源工具名（如 gh_run_view）。</summary>
    public required string SourceTool { get; init; }
    /// <summary>获取精炼工具名（如 gh_run_view 带 filter=error）。</summary>
    public required string RefineTool { get; init; }
    /// <summary>获取触发精炼的输出大小阈值（字节）。</summary>
    public long OutputSizeThreshold { get; init; } = 10_000;
    /// <summary>获取触发精炼的条件描述。</summary>
    public string? Description { get; init; }
}

/// <summary>
/// 工具精炼推荐器 — 根据输出大小/复杂度推荐精炼工具
/// </summary>
public sealed class ToolRefineRecommender {
    private readonly List<ToolRefineRule> _rules = [];

    /// <summary>添加精炼规则。</summary>
    public void AddRule(ToolRefineRule rule) => _rules.Add(rule);

    /// <summary>根据输出大小推荐精炼工具。返回推荐的工具名，无需精炼则返回 null。</summary>
    public string? RecommendRefine(string sourceTool, long outputSize) {
        foreach (var rule in _rules) {
            if (!string.Equals(rule.SourceTool, sourceTool, StringComparison.OrdinalIgnoreCase)) continue;
            if (outputSize >= rule.OutputSizeThreshold) return rule.RefineTool;
        }
        return null;
    }

    /// <summary>获取所有精炼规则。</summary>
    public IReadOnlyList<ToolRefineRule> Rules => _rules;
}
