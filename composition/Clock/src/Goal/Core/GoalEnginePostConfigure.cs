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
        _logger?.LogInformation("[GoalEngine] 已注册预定义 Graph 模板: refactor, bugfix, research, code_review, test_gen, negative_review_loop, cluster");
    }
}
