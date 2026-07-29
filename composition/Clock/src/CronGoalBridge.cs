
namespace Core.Goal;

public sealed partial class CronGoalBridge : IAsyncDisposable
{
    private readonly IGoalEngine _goalEngine;
    [Inject] private readonly ILogger<CronGoalBridge>? _logger;
    private readonly CronScheduler _scheduler;

    public bool IsStarted { get; private set; }

    public CronGoalBridge(ICronTaskStore taskStore, IGoalEngine goalEngine, ILogger<CronGoalBridge>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(taskStore);
        ArgumentNullException.ThrowIfNull(goalEngine);

        _goalEngine = goalEngine;
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

        await _scheduler.StartAsync(ct).ConfigureAwait(false);
        IsStarted = true;
        _logger?.LogInformation("[CronGoal] 桥接服务已启动");
    }

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
