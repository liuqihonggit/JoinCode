namespace Core.Prompts;

public sealed partial class ToolIdleReminderConfig
{
    public string ToolName { get; }
    public int TurnsSinceUse { get; }
    public int TurnsBetweenReminders { get; }
    public string ReminderMessage { get; }
    public Func<CancellationToken, ValueTask<string>>? StateProvider { get; }

    public ToolIdleReminderConfig(
        string toolName,
        int turnsSinceUse,
        int turnsBetweenReminders,
        string reminderMessage,
        Func<CancellationToken, ValueTask<string>>? stateProvider = null)
    {
        ToolName = toolName;
        TurnsSinceUse = turnsSinceUse;
        TurnsBetweenReminders = turnsBetweenReminders;
        ReminderMessage = reminderMessage;
        StateProvider = stateProvider;
    }
}

[Register]
public sealed partial class ToolIdleReminderService : IToolIdleReminderService
{
    private readonly Dictionary<string, int> _turnsSinceLastUse = [];
    private readonly Dictionary<string, int> _turnsSinceLastReminder = [];
    private readonly List<ToolIdleReminderConfig> _configs;
    private readonly ILogger<ToolIdleReminderService>? _logger;

    /// <summary>
    /// DI 构造函数 - 自动创建默认提醒配置
    /// </summary>
    public ToolIdleReminderService(
        ILogger<ToolIdleReminderService>? logger = null,
        ITodoService? todoService = null,
        ITaskService? taskService = null)
    {
        _logger = logger;
        _configs = CreateDefaultReminderConfigs(todoService, taskService);

        foreach (var config in _configs)
        {
            _turnsSinceLastUse[config.ToolName] = 0;
            _turnsSinceLastReminder[config.ToolName] = config.TurnsBetweenReminders;
        }
    }

    /// <summary>
    /// 测试用构造函数 - 允许自定义配置
    /// </summary>
    internal ToolIdleReminderService(
        IEnumerable<ToolIdleReminderConfig> configs,
        ILogger<ToolIdleReminderService>? logger = null)
    {
        _configs = [.. configs];
        _logger = logger;

        foreach (var config in _configs)
        {
            _turnsSinceLastUse[config.ToolName] = 0;
            _turnsSinceLastReminder[config.ToolName] = config.TurnsBetweenReminders;
        }
    }

    private static List<ToolIdleReminderConfig> CreateDefaultReminderConfigs(
        ITodoService? todoService, ITaskService? taskService)
    {
        return
        [
            new ToolIdleReminderConfig(
                TodoToolName.TodoWrite.ToValue(),
                turnsSinceUse: 10,
                turnsBetweenReminders: 10,
                reminderMessage: "The TodoWrite tool hasn't been used recently. If you're business on tasks, would benefit from using the TodoWrite tool. You consider cleaning up the todo list. Make sure that you NEVER mention this reminder to the user",
                stateProvider: async ct =>
                {
                    if (todoService is null) return string.Empty;
                    var result = await todoService.ListTodosAsync(includeCompleted: true, cancellationToken: ct).ConfigureAwait(false);
                    if (!result.Success || result.TotalCount == 0) return string.Empty;
                    var items = result.Todos.Select((t, i) => $"{i + 1}. [{t.Status}] {t.Content}");
                    return $"Here are the existing contents of your todo list:\n\n[{string.Join(", ", items)}]";
                }),
            new ToolIdleReminderConfig(
                TaskToolNameConstants.TaskUpdate,
                turnsSinceUse: 10,
                turnsBetweenReminders: 10,
                reminderMessage: "The task tools haven't been used recently. If you're business on tasks, would benefit from using task_create to add new tasks and task_update to update task status (set to in_progress when starting, completed and done). Consider cleaning up the task list. Make sure that you NEVER mention this reminder to the user",
                stateProvider: async ct =>
                {
                    if (taskService is null) return string.Empty;
                    var result = await taskService.ListTasksAsync(status: null, assignee: null, priority: null, limit: 50, offset: 0, cancellationToken: ct).ConfigureAwait(false);
                    if (result.Tasks.Count == 0) return string.Empty;
                    var items = result.Tasks.Select(t => $"#{t.Id}. [{t.Status}] {t.Title}");
                    return $"Here are the existing tasks:\n\n{string.Join("\n", items)}";
                }),
        ];
    }

    public void RecordAssistantTurn(string? toolNameUsed = null)
    {
        foreach (var config in _configs)
        {
            if (string.Equals(toolNameUsed, config.ToolName, StringComparison.OrdinalIgnoreCase))
            {
                _turnsSinceLastUse[config.ToolName] = 0;
            }
            else
            {
                _turnsSinceLastUse[config.ToolName]++;
            }

            _turnsSinceLastReminder[config.ToolName]++;
        }
    }

    public void RecordReminderSent(string toolName)
    {
        if (_turnsSinceLastReminder.ContainsKey(toolName))
        {
            _turnsSinceLastReminder[toolName] = 0;
        }
    }

    public async Task<IReadOnlyList<ToolIdleReminderResult>> CheckAndGenerateRemindersAsync(
        CancellationToken ct = default)
    {
        var results = new List<ToolIdleReminderResult>();

        foreach (var config in _configs)
        {
            var turnsSinceUse = _turnsSinceLastUse.GetValueOrDefault(config.ToolName, 0);
            var turnsSinceReminder = _turnsSinceLastReminder.GetValueOrDefault(config.ToolName, 0);

            if (turnsSinceUse < config.TurnsSinceUse || turnsSinceReminder < config.TurnsBetweenReminders)
            {
                continue;
            }

            string? stateContent = null;
            if (config.StateProvider is not null)
            {
                try
                {
                    stateContent = await config.StateProvider(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "获取工具 {ToolName} 状态时出错", config.ToolName);
                }
            }

            var message = string.IsNullOrEmpty(stateContent)
                ? config.ReminderMessage
                : string.Concat(config.ReminderMessage, "\n\n", stateContent);

            results.Add(new ToolIdleReminderResult(config.ToolName, message));
            RecordReminderSent(config.ToolName);

            _logger?.LogDebug("已生成工具空闲提醒: {ToolName}，距上次使用 {Turns} 回合",
                config.ToolName, turnsSinceUse);
        }

        return results;
    }

    public void Reset()
    {
        foreach (var config in _configs)
        {
            _turnsSinceLastUse[config.ToolName] = 0;
            _turnsSinceLastReminder[config.ToolName] = config.TurnsBetweenReminders;
        }
    }
}
