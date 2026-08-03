
namespace Core.Goal;

public sealed partial class CronGoalBridge : IAsyncDisposable
{
    private readonly IGoalEngine _goalEngine;
    private readonly ICronTaskStore _taskStore;
    private readonly IAgentDefinitionProvider? _agentDefinitionProvider;
    [Inject] private readonly ILogger<CronGoalBridge>? _logger;
    private readonly CronScheduler _scheduler;

    public bool IsStarted { get; private set; }

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

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsStarted) return;

        await _scheduler.StopAsync(ct).ConfigureAwait(false);
        IsStarted = false;
        _logger?.LogInformation("[CronGoal] 桥接服务已停止");
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.DisposeAsync().ConfigureAwait(false);
        IsStarted = false;
    }
}
