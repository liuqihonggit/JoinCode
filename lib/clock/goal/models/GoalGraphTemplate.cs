namespace Core.Goal;


/// <summary>
/// Goal Graph 模板 — 定义图结构 + 关键词匹配规则
/// </summary>
public sealed class GoalGraphTemplate
{
    /// <summary>模板名称，作为注册键</summary>
    public required string Name { get; init; }
    /// <summary>关键词列表，用于匹配目标描述</summary>
    public required string[] Keywords { get; init; }
    /// <summary>图构建委托 — 接收引擎实例与目标描述，返回 GoalGraph</summary>
    public required Func<GoalGraphEngine, string, GoalGraph> BuildGraph { get; init; }
    /// <summary>模板描述，可选</summary>
    public string? Description { get; init; }

    /// <summary>
    /// 判断目标描述是否匹配本模板的关键词
    /// </summary>
    /// <param name="objective">目标描述</param>
    /// <returns>匹配返回 true，否则 false</returns>
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
