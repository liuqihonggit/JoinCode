namespace Core.Goal;

/// <summary>
/// Goal 引擎后配置 — 在 DI 容器构建后注册预定义 Graph 模板
/// </summary>
public interface IGoalEnginePostConfigure
{
    /// <summary>
    /// 执行后配置 — 注册预定义 Graph 模板
    /// </summary>
    void Configure();
}

/// <summary>
/// GoalEnginePostConfigure 默认实现 — 调用 GoalGraphTemplates.RegisterAll 注册全部预定义模板
/// </summary>
public sealed class GoalEnginePostConfigure : IGoalEnginePostConfigure
{
    private readonly IGoalGraphTemplateRegistry _registry;
    private readonly ILogger<GoalEnginePostConfigure>? _logger;

    /// <summary>
    /// 构造 GoalEnginePostConfigure — 注入模板注册表与可选日志记录器
    /// </summary>
    /// <param name="registry">Goal Graph 模板注册表</param>
    /// <param name="logger">可选日志记录器</param>
    public GoalEnginePostConfigure(IGoalGraphTemplateRegistry registry, ILogger<GoalEnginePostConfigure>? logger = null)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Configure()
    {
        GoalGraphTemplates.RegisterAll(_registry);
        _logger?.LogInformation("[GoalEngine] 已注册预定义 Graph 模板: refactor, bugfix, research, code_review, test_gen, negative_review_loop, cluster");
    }
}
