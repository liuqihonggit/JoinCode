namespace Core.Goal;


/// <summary>
/// Goal Graph 模板 — 定义图结构 + 关键词匹配规则
/// </summary>
public sealed class GoalGraphTemplate
{
    public required string Name { get; init; }
    public required string[] Keywords { get; init; }
    public required Func<GoalGraphEngine, string, GoalGraph> BuildGraph { get; init; }
    public string? Description { get; init; }

    public bool MatchesObjective(string objective)
    {
        var lower = objective.ToLowerInvariant();
        foreach (var keyword in Keywords)
        {
            if (lower.Contains(keyword.ToLowerInvariant()))
                return true;
        }

        return false;
    }
}
