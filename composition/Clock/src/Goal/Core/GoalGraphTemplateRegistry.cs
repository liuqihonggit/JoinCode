namespace Core.Goal;

/// <summary>
/// Goal Graph 模板注册表默认实现 — 内部 ConcurrentDictionary，对外暴露遍历器 + 字典视图
/// </summary>
[Register(typeof(IGoalGraphTemplateRegistry), ServiceLifetime.Singleton)]
public sealed class GoalGraphTemplateRegistry : ServiceEntity, IGoalGraphTemplateRegistry
{
    private readonly ConcurrentDictionary<string, GoalGraphTemplate> _templates = new(StringComparer.Ordinal);

    public void Register(GoalGraphTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        _templates.TryAdd(template.Name, template);
    }

    public GoalGraphTemplate? FindMatch(string objective)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);
        foreach (var template in _templates.Values)
        {
            if (template.MatchesObjective(objective))
                return template;
        }
        return null;
    }

    public IEnumerable<GoalGraphTemplate> GetAll() => _templates.Values;
}
