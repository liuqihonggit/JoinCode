namespace Core.Goal;

/// <summary>
/// Goal Graph 模板注册表 — 管理预定义模板，根据 objective 自动匹配
/// </summary>
public interface IGoalGraphTemplateRegistry
{
    void Register(GoalGraphTemplate template);
    GoalGraphTemplate? FindMatch(string objective);
    IEnumerable<GoalGraphTemplate> GetAll();
}
