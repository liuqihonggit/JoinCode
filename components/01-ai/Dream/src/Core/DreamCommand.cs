
namespace JoinCode.Dream.Commands;

/// <summary>
/// /dream 命令 - 手动触发记忆整合
/// </summary>
[Command(Name = "dream", Description = "手动触发记忆整合（做梦）", Usage = "/dream [force]")]
public sealed partial class DreamCommand : ICommand
{
    private readonly IDreamFeature _dreamFeature;
    [Inject] private readonly ILogger<DreamCommand>? _logger;

    public DreamCommand(
        IDreamFeature dreamFeature,
        ILogger<DreamCommand>? logger = null)
    {
        _dreamFeature = dreamFeature ?? throw new ArgumentNullException(nameof(dreamFeature));
        _logger = logger;
    }

    public string Name => "dream";
    public string Description => "手动触发记忆整合（做梦）";
    public string Usage => "/dream [force]";

    public async Task ExecuteAsync(ICommandContext context, CancellationToken cancellationToken = default)
    {
        var force = context.Arguments.Length > 0 &&
                    context.Arguments[0].Equals("force", StringComparison.OrdinalIgnoreCase);

        context.Output("=== 开始记忆整合（做梦） ===");
        context.Output(force ? "模式: 强制触发" : "模式: 自动门控");
        context.Output("");

        try
        {
            // 执行做梦 - 使用功能接口
            var result = await _dreamFeature.ExecuteAsync(
                new DreamRequest(Force: force),
                cancellationToken).ConfigureAwait(false);

            if (result.IsSkipped)
            {
                context.OutputWarning($"做梦被跳过: {result.Content}");
                return;
            }

            if (!result.IsSuccess)
            {
                context.OutputError($"做梦失败: {result.Content}");
                return;
            }

            if (!string.IsNullOrEmpty(result.Content))
            {
                context.OutputSuccess("做梦完成！");
                context.Output($"任务ID: {result.TaskId}");
                context.Output($"处理会话: {result.SessionsProcessed}");
                context.Output($"耗时: {result.ExecutionTimeMs}ms");
                context.Output("");
                context.Output("整合结果:");
                context.Output(result.Content);
            }
            else
            {
                context.OutputWarning("做梦未产生结果");
            }
        }
        catch (OperationCanceledException)
        {
            context.OutputWarning("做梦任务已取消");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "执行做梦命令失败");
            context.OutputError($"做梦失败: {ex.Message}");
        }
    }
}

/// <summary>
/// 做梦任务管理命令
/// </summary>
[Command(Name = "dream-tasks", Description = "查看做梦任务状态", Usage = "/dream-tasks [list|kill <taskId>]")]
public sealed partial class DreamTasksCommand : ICommand
{
    private readonly IDreamFeature _dreamFeature;
    [Inject] private readonly ILogger<DreamTasksCommand>? _logger;

    public DreamTasksCommand(
        IDreamFeature dreamFeature,
        ILogger<DreamTasksCommand>? logger = null)
    {
        _dreamFeature = dreamFeature ?? throw new ArgumentNullException(nameof(dreamFeature));
        _logger = logger;
    }

    public string Name => "dream-tasks";
    public string Description => "查看做梦任务状态";
    public string Usage => "/dream-tasks [list|kill <taskId>]";

    public async Task ExecuteAsync(ICommandContext context, CancellationToken cancellationToken = default)
    {
        var action = context.Arguments.Length > 0 ? context.Arguments[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case "list":
                await ListTasksAsync(context, cancellationToken).ConfigureAwait(false);
                break;
            case "kill":
                await KillTaskAsync(context, cancellationToken).ConfigureAwait(false);
                break;
            default:
                context.OutputError($"未知操作: {action}");
                context.Output("可用操作: list, kill <taskId>");
                break;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ListTasksAsync(ICommandContext context, CancellationToken cancellationToken)
    {
        var tasks = await _dreamFeature.ListTasksAsync(cancellationToken).ConfigureAwait(false);

        if (tasks.Count == 0)
        {
            context.Output("当前没有做梦任务");
            return;
        }

        context.Output($"=== 做梦任务列表 ({tasks.Count}) ===");
        context.Output("");

        foreach (var (id, task) in tasks.OrderByDescending(t => t.Value.StartTime))
        {
            var duration = task.EndTime.HasValue
                ? task.EndTime.Value - task.StartTime
                : DateTime.UtcNow - task.StartTime;

            context.Output($"[{id}] {task.Status}");
            context.Output($"  阶段: {task.Phase}");
            context.Output($"  会话: {task.SessionsReviewing}");
            context.Output($"  文件: {task.FilesTouched.Count}");
            context.Output($"  回合: {task.Turns.Count}");
            context.Output($"  耗时: {duration.TotalSeconds:F1}s");

            if (task.Turns.Count > 0)
            {
                var lastTurn = task.Turns[^1];
                context.Output($"  最新: {lastTurn.Text[..Math.Min(50, lastTurn.Text.Length)]}...");
            }

            context.Output("");
        }
    }

    private async Task KillTaskAsync(ICommandContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Length < 2)
        {
            context.OutputError("用法: /dream-tasks kill <taskId>");
            return;
        }

        var taskId = context.Arguments[1];
        var task = await _dreamFeature.GetTaskStatusAsync(taskId, cancellationToken).ConfigureAwait(false);

        if (task == null)
        {
            context.OutputError($"任务不存在: {taskId}");
            return;
        }

        if (task.IsTerminal)
        {
            context.OutputWarning($"任务 {taskId} 已处于终态 ({task.Status})");
            return;
        }

        context.Output($"正在终止任务 {taskId}...");
        await _dreamFeature.KillTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        context.OutputSuccess($"任务 {taskId} 已终止");
    }
}
