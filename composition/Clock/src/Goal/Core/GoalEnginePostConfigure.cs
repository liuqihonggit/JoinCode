namespace Core.Goal;

/// <summary>
/// Goal 引擎后配置 — 在 DI 容器构建后注册预定义 Graph 模板
/// </summary>
public interface IGoalEnginePostConfigure
{
    void Configure();
}

public sealed class GoalEnginePostConfigure : IGoalEnginePostConfigure
{
    private readonly IGoalGraphTemplateRegistry _registry;
    [Inject] private readonly ILogger<GoalEnginePostConfigure>? _logger;

    public GoalEnginePostConfigure(IGoalGraphTemplateRegistry registry, ILogger<GoalEnginePostConfigure>? logger = null)
    {
        _registry = registry;
        _logger = logger;
    }

    public void Configure()
    {
        GoalGraphTemplates.RegisterAll(_registry);
<<<<<<< HEAD:composition/Clock/src/Goal/Core/GoalEnginePostConfigure.cs
        _logger?.LogInformation("[GoalEngine] 已注册预定义 Graph 模板: refactor, bugfix, research, code_review, test_gen, negative_review_loop");
=======
        _logger?.LogInformation("[GoalEngine] 已注册预定义 Graph 模板: refactor, bugfix, research");
>>>>>>> c0bbb415c3daaa0e27b22a271cafbff47cad1d13:composition/Clock/src/Goal/Goal/Core/GoalEnginePostConfigure.cs
    }
}
