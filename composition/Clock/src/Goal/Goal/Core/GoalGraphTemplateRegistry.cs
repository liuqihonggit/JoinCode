namespace Core.Goal;

/// <summary>
/// Goal Graph 模板注册表默认实现
/// </summary>
[Register]
public sealed class GoalGraphTemplateRegistry : IGoalGraphTemplateRegistry
{
    private readonly List<GoalGraphTemplate> _templates = [];
    private readonly object _lock = new();

    public void Register(GoalGraphTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        lock (_lock)
        {
            _templates.Add(template);
        }
    }

    public GoalGraphTemplate? FindMatch(string objective)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);
        lock (_lock)
        {
            foreach (var template in _templates)
            {
                if (template.MatchesObjective(objective))
                    return template;
            }
        }

        return null;
    }

    public IReadOnlyList<GoalGraphTemplate> GetAll()
    {
        lock (_lock)
        {
            return _templates.ToArray();
        }
    }
}
