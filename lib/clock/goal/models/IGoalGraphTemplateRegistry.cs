namespace Core.Goal;

/// <summary>
/// Goal Graph 模板注册表 — 管理预定义模板，根据 objective 自动匹配
/// </summary>
public interface IGoalGraphTemplateRegistry {
    /// <summary>
    /// 注册 Goal Graph 模板
    /// </summary>
    /// <param name="template">待注册的模板</param>
    void Register(GoalGraphTemplate template);

    /// <summary>
    /// 根据目标描述查找匹配的模板
    /// </summary>
    /// <param name="objective">目标描述</param>
    /// <returns>匹配的模板，无匹配返回 null</returns>
    GoalGraphTemplate? FindMatch(string objective);

    /// <summary>
    /// 获取全部已注册模板
    /// </summary>
    /// <returns>全部模板的枚举器</returns>
    IEnumerable<GoalGraphTemplate> GetAll();
}