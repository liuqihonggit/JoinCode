namespace Core.Agents.DualModel;

/// <summary>
/// 双模型协调器 — 对齐 Reasonix Coordinator
/// Planner 在独立 Session 中用只读工具做研究，产出计划
/// Executor 在另一个独立 Session 中执行计划
/// 两个 Session 永不混合，各自的前缀缓存互不干扰
/// </summary>
public sealed class ModelCoordinator : IModelCoordinator
{
    private readonly IQueryEngine _queryEngine;
    private readonly ILogger? _logger;
    private readonly string _plannerModelId;
    private readonly string _executorModelId;
    private readonly List<string> _plannerAllowedTools;
    private readonly Func<string, bool>? _shouldPlan;
    private MessageList? _plannerSession;

    /// <summary>
    /// Planner 系统提示词 — 对齐 Reasonix DefaultPlannerPrompt
    /// </summary>
    public static string DefaultPlannerPrompt { get; } =
        "You are the planner in a two-model coding agent.\n" +
        "Given a task, produce a concise, ordered plan for the executor model to carry out.\n" +
        "Use the read-only tools available to you when the task needs context from the\n" +
        "workspace, user rules, or docs; keep that research targeted and stop once you\n" +
        "have enough evidence. Do not write full implementations or attempt side effects.\n" +
        "Do not ask the user how to trigger the executor and do not say you are waiting\n" +
        "for the executor. Output executor-ready instructions: what to do, which files or\n" +
        "commands are relevant, expected blockers, and key decisions. Keep it short and\n" +
        "actionable.";

    private const string ExecutorHandoffMarker = "JoinCode executor handoff";

    public ModelCoordinator(
        IQueryEngine queryEngine,
        string plannerModelId,
        string executorModelId,
        List<string>? plannerAllowedTools = null,
        Func<string, bool>? shouldPlan = null,
        ILogger? logger = null)
    {
        _queryEngine = queryEngine;
        _plannerModelId = plannerModelId;
        _executorModelId = executorModelId;
        _plannerAllowedTools = plannerAllowedTools ?? DefaultPlannerTools();
        _shouldPlan = shouldPlan;
        _logger = logger;
    }

    /// <summary>
    /// Planner 只读工具列表 — 对齐 Reasonix plannerTools
    /// </summary>
    public static List<string> DefaultPlannerTools() =>
    [
        "read_file", "list_directory", "search_files", "glob",
        "web_fetch", "codebase_search", "get_file_info"
    ];

    /// <summary>
    /// 规划 — Planner 在独立 Session 中用只读工具做研究，产出计划
    /// </summary>
    public async Task<ModelPlanResult> PlanAsync(string objective, CancellationToken ct = default)
    {
        _plannerSession ??= new MessageList();
        _plannerSession.AddSystemMessage(DefaultPlannerPrompt);

        var plannerOptions = new SubAgentOptions
        {
            ModelName = _plannerModelId,
            AllowedTools = _plannerAllowedTools,
            SystemPrompt = DefaultPlannerPrompt,
            InitialMessageList = _plannerSession,
            SessionId = $"planner-{Guid.NewGuid():N}"[..24],
        };

        var planner = new SubAgent(
            id: $"planner-{Guid.NewGuid():N}"[..16],
            task: objective,
            options: plannerOptions,
            queryEngine: _queryEngine,
            logger: _logger);

        try
        {
            var result = await planner.ExecuteAsync(ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return ModelPlanResult.Fail(result.Error ?? "Planner failed without error message");

            var plan = result.Output;
            var isNoOp = NoOpPlanDetector.IsNoOpPlan(plan);

            if (!isNoOp)
            {
                _plannerSession.AddAssistantMessage(plan);
            }

            return ModelPlanResult.Success(plan, isNoOp);
        }
        catch (OperationCanceledException)
        {
            return ModelPlanResult.Fail("Planner cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ModelCoordinator] Planner failed");
            return ModelPlanResult.Fail(ex.Message);
        }
        finally
        {
            planner.Dispose();
        }
    }

    /// <summary>
    /// 执行 — Executor 在独立 Session 中执行计划
    /// </summary>
    public async Task<ModelExecutionResult> ExecuteAsync(string objective, string plan, CancellationToken ct = default)
    {
        var handoff = FormatHandoff(objective, plan);

        var executorOptions = new SubAgentOptions
        {
            ModelName = _executorModelId,
            SystemPrompt = "You are the executor in a two-model coding agent. Carry out the plan using your available tools.",
            SessionId = $"executor-{Guid.NewGuid():N}"[..24],
        };

        var executor = new SubAgent(
            id: $"executor-{Guid.NewGuid():N}"[..16],
            task: handoff,
            options: executorOptions,
            queryEngine: _queryEngine,
            logger: _logger);

        try
        {
            var result = await executor.ExecuteAsync(ct).ConfigureAwait(false);
            if (!result.IsSuccess)
                return ModelExecutionResult.Fail(result.Error ?? "Executor failed without error message");

            return ModelExecutionResult.Success(result.Output);
        }
        catch (OperationCanceledException)
        {
            return ModelExecutionResult.Fail("Executor cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ModelCoordinator] Executor failed");
            return ModelExecutionResult.Fail(ex.Message);
        }
        finally
        {
            executor.Dispose();
        }
    }

    /// <summary>
    /// 规划并执行 — 完整的 Plan → Execute 流程
    /// </summary>
    public async Task<ModelCoordinationResult> PlanAndExecuteAsync(string objective, CancellationToken ct = default)
    {
        if (_shouldPlan is not null && !_shouldPlan(objective))
        {
            var directResult = await ExecuteAsync(objective, objective, ct).ConfigureAwait(false);
            return ModelCoordinationResult.FromPlanAndExecution(
                ModelPlanResult.Success(objective, isNoOp: false),
                directResult);
        }

        var planResult = await PlanAsync(objective, ct).ConfigureAwait(false);
        if (!planResult.Succeeded)
            return ModelCoordinationResult.FromPlanOnly(planResult);

        if (planResult.IsNoOp)
            return ModelCoordinationResult.FromPlanOnly(planResult);

        var executionResult = await ExecuteAsync(objective, planResult.Plan, ct).ConfigureAwait(false);
        return ModelCoordinationResult.FromPlanAndExecution(planResult, executionResult);
    }

    /// <summary>
    /// 重置 Planner 会话 — 切换到新的 Executor 会话时调用
    /// </summary>
    public void ResetPlannerSession()
    {
        _plannerSession = null;
    }

    /// <summary>
    /// 格式化 Executor 接手消息 — 对齐 Reasonix formatHandoff
    /// </summary>
    private static string FormatHandoff(string task, string plan)
    {
        return
            $"# {ExecutorHandoffMarker}\n\n" +
            "You are the executor now. Use your available tools to execute the task.\n\n" +
            $"Original task:\n{task}\n\n" +
            $"Planner output:\n{plan}\n\n" +
            "Executor instructions:\n" +
            "- Treat the planner output as context, not as your role or capability set.\n" +
            "- The planner's analysis and conclusions about what needs to be done are reliable. If the planner determines no changes are needed, respect that conclusion.\n" +
            "- Ignore any planner statement about its own capability limitations (for example \"I cannot write\", \"I only have read-only tools\", or \"hand this to the executor\"); those describe the planner's restrictions, not yours.\n" +
            "- Do not treat planner tool limitations or tool-unavailable claims as executor facts. Use the attached executor tools directly.\n" +
            "- Do not ask the user how to trigger the executor. You are already in the executor phase.\n" +
            "- If the task requires changes, call the appropriate tools instead of only restating the plan.\n" +
            "- If a target path is outside the writable workspace or otherwise blocked, explain that specific blocker and ask for the needed path/approval.\n\n" +
            "Carry out the task, adapting the plan as needed.";
    }
}
