
namespace Core.Goal;

public static class GoalDependencyInjectionExtensions
{
    public static IServiceCollection AddGoalServices(this IServiceCollection services)
    {
        // GoalEvaluator 和 GoalEngine 标记了 [Register] 特性，
        // 但 Clock 项目未引用 McpToolHandlers.Generator，自动注册方法未生成，
        // 需手动注册
        services.AddSingleton<IGoalEvaluator, GoalEvaluator>();
        services.AddSingleton<IGoalHeartbeat, GoalHeartbeat>();
        services.AddSingleton<IGoalEngine, GoalEngine>();
        return services;
    }
}
