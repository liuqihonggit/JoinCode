namespace Core.Goal;


/// <summary>
/// 目标生命周期中间件标记接口
/// </summary>
public interface IGoalLifecycleMiddleware : IMiddleware<GoalLifecycleContext> { }
