
namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Cron)]
public class CronToolHandlers
{
    private const int MaxTasks = 50;

    private static readonly string[] CommonCronPatterns =
    [
        "*/5 * * * *", "*/15 * * * *", "*/30 * * * *",
        "0 * * * *", "0 */6 * * *", "0 9 * * *",
        "0 9 * * 1-5", "0 0 * * *"
    ];

    private static readonly FrozenDictionary<string, string> CronHumanMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["*/5 * * * *"] = "every 5 minutes",
        ["*/10 * * * *"] = "every 10 minutes",
        ["*/15 * * * *"] = "every 15 minutes",
        ["*/30 * * * *"] = "every 30 minutes",
        ["0 * * * *"] = "every hour",
        ["0 */2 * * *"] = "every 2 hours",
        ["0 */6 * * *"] = "every 6 hours",
        ["0 9 * * *"] = "every day at 9:00",
        ["0 0 * * *"] = "every day at midnight",
        ["0 9 * * 1-5"] = "every weekday at 9:00",
        ["0 9 * * 1"] = "every Monday at 9:00",
        ["0 0 1 * *"] = "every month on the 1st",
    }.ToFrozenDictionary();

    private readonly ICronTaskStore _taskStore;
    private readonly ICronSchedulerRef? _schedulerRef;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly IClockService _clock;

    public CronToolHandlers(ICronTaskStore taskStore, ICronSchedulerRef? schedulerRef = null, ISubAgentContextAccessor? subAgentContextAccessor = null, IClockService? clock = null)
    {
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _schedulerRef = schedulerRef;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
    }

    [McpTool(CronToolNameConstants.CronCreate, "Create a scheduled task that runs at specified intervals using cron syntax", "cron")]
    public async Task<ToolResult> CreateCronTaskAsync(
        [McpToolParameter("Cron expression (5 fields: minute hour day month weekday, e.g. \"0 9 * * *\" for daily at 9am)")] string cron,
        [McpToolParameter("The prompt/instruction to execute when the task fires")] string prompt,
        [McpToolParameter("Whether this is a recurring task (default: true). Set to false for one-shot tasks.", Required = false)] bool? recurring = true,
        [McpToolParameter("Whether to persist the task to disk (default: false). Session-only tasks are lost when the session ends.", Required = false)] bool? durable = false,
        CancellationToken cancellationToken = default)
    {
        if (!CronExpressionParser.IsValid(cron))
        {
            return McpResultBuilder.Error()
                .WithText($"Invalid cron expression: {cron}\nFormat: minute hour day month weekday\nExamples: \"0 9 * * *\" (daily 9am), \"0 */6 * * *\" (every 6h), \"0 9 * * 1-5\" (weekdays 9am)")
                .Build();
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return McpResultBuilder.Error()
                .WithText("prompt cannot be empty")
                .Build();
        }

        var isRecurring = recurring ?? true;
        var isDurable = durable ?? false;

        // 对齐 TS: teammate 不允许创建 durable 任务 — teammate 不跨会话持久化
        var agentId = _subAgentContextAccessor.Current?.AgentId;
        if (agentId is not null && isDurable)
        {
            return McpResultBuilder.Error()
                .WithText("Sub-agents cannot create durable (persisted) cron tasks. Durable tasks persist across sessions but sub-agents do not.")
                .Build();
        }

        var nextRun = CronJitterHelper.NextCronRunMs(cron, _clock.GetUtcNowOffset().ToUnixTimeMilliseconds());
        if (nextRun == null)
        {
            return McpResultBuilder.Error()
                .WithText($"Cron expression \"{cron}\" does not match any date in the next year. Please verify the expression is correct.")
                .Build();
        }

        var existingTasks = await _taskStore.GetAllTasksAsync(cancellationToken).ConfigureAwait(false);
        if (existingTasks.Count >= MaxTasks)
        {
            return McpResultBuilder.Error()
                .WithText($"Maximum number of scheduled tasks reached ({MaxTasks}). Delete existing tasks before creating new ones.")
                .Build();
        }

        var request = new CreateCronTaskRequest
        {
            CronExpression = cron,
            Prompt = prompt,
            IsRecurring = isRecurring,
            IsDurable = isDurable,
            AgentId = agentId, // 对齐 TS: 传入 teammate 的 agentId
        };

        var task = await _taskStore.AddTaskAsync(request, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: 创建后通知调度器刷新 — CronScheduler 的 Timer 会在下一个 tick 检测到新任务
        _schedulerRef?.NotifyTaskChanged();

        var humanSchedule = CronToHuman(cron);
        var response = new StringBuilder();
        response.Append($"Created scheduled task {task.Id}: {humanSchedule} ({(isRecurring ? "recurring" : "one-shot")})");

        if (isDurable)
        {
            response.Append(" [persisted to disk]");
        }
        else
        {
            response.Append(" [session-only]");
        }

        if (agentId is not null)
        {
            response.Append($" [agent: {agentId}]");
        }

        if (nextRun != null)
        {
            var nextTime = DateTimeOffset.FromUnixTimeMilliseconds(nextRun.Value).ToLocalTime();
            response.AppendLine();
            response.Append($"Next run: {nextTime:yyyy-MM-dd HH:mm}");
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(CronToolNameConstants.CronList, "List all scheduled tasks", "cron")]
    public async Task<ToolResult> ListCronTasksAsync(
        CancellationToken cancellationToken = default)
    {
        var allTasks = await _taskStore.GetAllTasksAsync(cancellationToken).ConfigureAwait(false);

        // 对齐 TS: teammate 只看自己的 cron 任务 — 按 agentId 过滤
        var agentId = _subAgentContextAccessor.Current?.AgentId;
        var tasks = agentId is not null
            ? allTasks.Where(t => t.AgentId == agentId).ToList()
            : allTasks;

        if (tasks.Count == 0)
        {
            return McpResultBuilder.Success()
                .WithText("No scheduled tasks")
                .Build();
        }

        var response = new StringBuilder();
        response.AppendLine($"Scheduled Tasks ({tasks.Count})");
        response.AppendLine();

        foreach (var task in tasks.OrderBy(t => t.CreatedAt))
        {
            var humanSchedule = CronToHuman(task.CronExpression);
            var promptDisplay = task.Prompt.Length > 80
                ? string.Concat(task.Prompt.AsSpan(0, 77), "...")
                : task.Prompt;

            var durability = task.IsDurable ? "persisted" : "session-only";
            var type = task.IsRecurring ? "recurring" : "one-shot";
            var owner = task.AgentId is not null ? $" [agent: {task.AgentId}]" : "";

            response.AppendLine($"{task.Id} — {humanSchedule} ({type}) [{durability}]{owner}: {promptDisplay}");
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(CronToolNameConstants.CronDelete, "Delete a scheduled task by ID", "cron")]
    public async Task<ToolResult> DeleteCronTaskAsync(
        [McpToolParameter("The ID of the scheduled task to delete")] string task_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task_id))
        {
            return McpResultBuilder.Error()
                .WithText("task_id cannot be empty")
                .Build();
        }

        var existingTasks = await _taskStore.GetAllTasksAsync(cancellationToken).ConfigureAwait(false);
        var task = existingTasks.FirstOrDefault(t => t.Id == task_id);
        if (task is null)
        {
            return McpResultBuilder.Error()
                .WithText($"Scheduled task {task_id} not found")
                .Build();
        }

        // 对齐 TS: teammate 只能删除自己的 cron 任务 — 校验所有权
        var agentId = _subAgentContextAccessor.Current?.AgentId;
        if (agentId is not null && task.AgentId != agentId)
        {
            return McpResultBuilder.Error()
                .WithText($"Cannot delete cron job '{task_id}': owned by another agent")
                .Build();
        }

        await _taskStore.RemoveTasksAsync([task_id], cancellationToken).ConfigureAwait(false);

        // 对齐 TS: 删除后通知调度器刷新
        _schedulerRef?.NotifyTaskChanged();

        return McpResultBuilder.Success()
            .WithText($"Cancelled job {task_id}")
            .Build();
    }

    [McpTool(CronToolNameConstants.CronValidate, "Validate a cron expression and show its parsed fields", "cron")]
    public Task<ToolResult> ValidateCronExpressionAsync(
        [McpToolParameter("Cron expression to validate")] string cron,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            return Task.FromResult(McpResultBuilder.Error()
                .WithText("cron cannot be empty")
                .Build());
        }

        var fields = CronExpressionParser.Parse(cron);
        if (fields == null)
        {
            return Task.FromResult(McpResultBuilder.Error()
                .WithText($"Invalid cron expression: {cron}\n\nFormat: minute hour day month weekday\nExamples:\n- \"0 9 * * *\" daily at 9am\n- \"0 */6 * * *\" every 6 hours\n- \"0 9 * * 1-5\" weekdays at 9am")
                .Build());
        }

        var humanSchedule = CronToHuman(cron);
        var response = new StringBuilder();
        response.AppendLine($"Valid cron expression: {humanSchedule}");
        response.AppendLine();
        response.AppendLine($"Parsed fields:");
        response.AppendLine($"  Minute: {string.Join(",", fields.Minute)}");
        response.AppendLine($"  Hour: {string.Join(",", fields.Hour)}");
        response.AppendLine($"  Day: {string.Join(",", fields.DayOfMonth)}");
        response.AppendLine($"  Month: {string.Join(",", fields.Month)}");
        response.AppendLine($"  Weekday: {string.Join(",", fields.DayOfWeek)}");

        var nextRun = CronJitterHelper.NextCronRunMs(cron, _clock.GetUtcNowOffset().ToUnixTimeMilliseconds());
        if (nextRun != null)
        {
            var nextTime = DateTimeOffset.FromUnixTimeMilliseconds(nextRun.Value).ToLocalTime();
            response.AppendLine();
            response.AppendLine($"Next run: {nextTime:yyyy-MM-dd HH:mm}");
        }

        return Task.FromResult(McpResultBuilder.Success()
            .WithText(response.ToString())
            .Build());
    }

    private static string CronToHuman(string cron)
    {
        if (CronHumanMap.TryGetValue(cron, out var human))
        {
            return human;
        }

        var parts = cron.Split(' ');
        if (parts.Length != 5) return cron;

        var sb = new StringBuilder();

        if (parts[0].StartsWith("*/"))
        {
            sb.Append($"every {parts[0][2..]} minutes");
        }
        else if (parts[1].StartsWith("*/"))
        {
            sb.Append($"every {parts[1][2..]} hours");
        }
        else if (parts[2] == "*" && parts[3] == "*" && parts[4] == "*")
        {
            sb.Append($"daily at {parts[1]}:{parts[0].PadLeft(2, '0')}");
        }
        else
        {
            sb.Append(cron);
        }

        return sb.ToString();
    }
}
