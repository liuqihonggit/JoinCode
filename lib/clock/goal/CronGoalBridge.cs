
namespace Core.Goal;

/// <summary>
/// Cron 任务与 Goal 引擎的桥接服务 — 将定时任务触发转换为 Goal 引擎启动
/// </summary>
public sealed partial class CronGoalBridge : IAsyncDisposable
{
    private readonly IGoalEngine _goalEngine;
    private readonly ICronTaskStore _taskStore;
    private readonly IAgentDefinitionProvider? _agentDefinitionProvider;
    private readonly ILogger<CronGoalBridge>? _logger;
    private readonly CronScheduler _scheduler;
    private int _disposed;

    /// <summary>桥接服务是否已启动</summary>
    public bool IsStarted { get; private set; }

    /// <summary>
    /// 构造 CronGoalBridge — 注入任务存储、目标引擎、可选 Agent 定义提供器与日志记录器
    /// </summary>
    /// <param name="taskStore">Cron 任务存储</param>
    /// <param name="goalEngine">目标引擎</param>
    /// <param name="agentDefinitionProvider">可选 Agent 定义提供器，用于自动注册后台 Agent 的 Cron 任务</param>
    /// <param name="logger">可选日志记录器</param>
    public CronGoalBridge(ICronTaskStore taskStore, IGoalEngine goalEngine, IAgentDefinitionProvider? agentDefinitionProvider = null, ILogger<CronGoalBridge>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(taskStore);
        ArgumentNullException.ThrowIfNull(goalEngine);

        _taskStore = taskStore;
        _goalEngine = goalEngine;
        _agentDefinitionProvider = agentDefinitionProvider;
        _logger = logger;
        _scheduler = new CronScheduler(new CronSchedulerOptions
        {
            OnFire = HandleCronFireAsync,
            JitterConfig = CronJitterConfig.Default
        }, taskStore);
    }

    internal async Task HandleCronFireAsync(CronTask task)
    {
        _logger?.LogInformation("[CronGoal] 任务触发: {TaskId} - {Prompt}", task.Id, task.Prompt);

        if (_goalEngine.IsRunning)
        {
            _logger?.LogWarning("[CronGoal] 目标引擎正在运行，跳过定时任务: {TaskId}", task.Id);
            return;
        }

        try
        {
            await _goalEngine.StartAsync(task.Prompt).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("已有目标正在运行"))
        {
            _logger?.LogWarning("[CronGoal] 目标引擎已被占用，跳过定时任务: {TaskId} - {Error}", task.Id, ex.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[CronGoal] 启动目标失败: {TaskId}", task.Id);
        }
    }

    /// <summary>
    /// 启动桥接服务 — 注册后台 Agent Cron 任务并启动调度器
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsStarted) return;

        await RegisterBackgroundAgentCronTasksAsync(ct).ConfigureAwait(false);
        await _scheduler.StartAsync(ct).ConfigureAwait(false);
        IsStarted = true;
        _logger?.LogInformation("[CronGoal] 桥接服务已启动");
    }

    /// <summary>
    /// 扫描后台 Agent 定义，为标记 is_background 的 Agent 自动注册 Cron 定时任务
    /// </summary>
    private async Task RegisterBackgroundAgentCronTasksAsync(CancellationToken ct)
    {
        if (_agentDefinitionProvider is null)
            return;

        try
        {
            var definitions = await _agentDefinitionProvider.GetAgentDefinitionsAsync(cancellationToken: ct).ConfigureAwait(false);
            var backgroundAgents = definitions.Where(d => d.IsBackground).ToList();

            foreach (var agent in backgroundAgents)
            {
                var existingTasks = await _taskStore.GetAllTasksAsync(ct).ConfigureAwait(false);
                var alreadyRegistered = existingTasks.Any(t => t.Prompt.Contains(agent.DisplayId, StringComparison.OrdinalIgnoreCase));

                if (alreadyRegistered)
                    continue;

                var cronExpr = GetCronForAgent(agent.DisplayId);
                var prompt = BuildBackgroundAgentPrompt(agent);

                var request = new CreateCronTaskRequest
                {
                    CronExpression = cronExpr,
                    Prompt = prompt,
                    IsRecurring = true,
                    IsDurable = true
                };

                await _taskStore.AddTaskAsync(request, ct).ConfigureAwait(false);
                _logger?.LogInformation("[CronGoal] 已为后台 Agent '{DisplayId}' 注册 Cron 任务: {Cron}", agent.DisplayId, cronExpr);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[CronGoal] 注册后台 Agent Cron 任务失败");
        }
    }

    private static string GetCronForAgent(string displayId) => displayId switch
    {
        "executor:doctor" => "0 */12 * * *",
        _ => "0 */12 * * *"
    };

    private static string BuildBackgroundAgentPrompt(JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition agent) =>
        $"使用 {agent.DisplayId} Agent 执行后台维护任务：{agent.WhenToUse}";

    /// <summary>
    /// 停止桥接服务 — 停止调度器并标记为未启动
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsStarted) return;

        await _scheduler.StopAsync(ct).ConfigureAwait(false);
        IsStarted = false;
        _logger?.LogInformation("[CronGoal] 桥接服务已停止");
    }

    /// <summary>
    /// 异步释放 — 释放调度器并标记为未启动
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _ = _scheduler.DisposeAsync();
        IsStarted = false;
        return ValueTask.CompletedTask;
    }
}
