namespace Core.Prompts;

/// <summary>
/// 工具空闲提醒配置 — 描述单个工具的空闲检测阈值与提醒文案。
/// </summary>
public sealed partial class ToolIdleReminderConfig {
    /// <summary>
    /// 工具名称。
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// 触发提醒所需的最小空闲回合数。
    /// </summary>
    public int TurnsSinceUse { get; }

    /// <summary>
    /// 两次提醒之间的最小回合间隔。
    /// </summary>
    public int TurnsBetweenReminders { get; }

    /// <summary>
    /// 提醒文案。
    /// </summary>
    public string ReminderMessage { get; }

    /// <summary>
    /// 状态提供者 — 可选，用于附加当前工具状态到提醒文案。
    /// </summary>
    public Func<CancellationToken, ValueTask<string>>? StateProvider { get; }

    /// <summary>
    /// 构造工具空闲提醒配置。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="turnsSinceUse">触发提醒所需的最小空闲回合数。</param>
    /// <param name="turnsBetweenReminders">两次提醒之间的最小回合间隔。</param>
    /// <param name="reminderMessage">提醒文案。</param>
    /// <param name="stateProvider">状态提供者，可选。</param>
    public ToolIdleReminderConfig(
        string toolName,
        int turnsSinceUse,
        int turnsBetweenReminders,
        string reminderMessage,
        Func<CancellationToken, ValueTask<string>>? stateProvider = null) {
        ToolName = toolName;
        TurnsSinceUse = turnsSinceUse;
        TurnsBetweenReminders = turnsBetweenReminders;
        ReminderMessage = reminderMessage;
        StateProvider = stateProvider;
    }
}

/// <summary>
/// 工具空闲提醒服务 — 监控工具调用间隔，超时触发提醒。
/// </summary>
[Register(typeof(IToolIdleReminderService), ServiceLifetime.Singleton)]
public sealed partial class ToolIdleReminderService : ServiceEntity, IToolIdleReminderService {
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
        ITaskService? taskService = null) {
        _logger = logger;
        _configs = CreateDefaultReminderConfigs(todoService, taskService);

        foreach (var config in _configs) {
            _turnsSinceLastUse[config.ToolName] = 0;
            _turnsSinceLastReminder[config.ToolName] = config.TurnsBetweenReminders;
        }
    }

    /// <summary>
    /// 测试用构造函数 - 允许自定义配置
    /// </summary>
    internal ToolIdleReminderService(
        IEnumerable<ToolIdleReminderConfig> configs,
        ILogger<ToolIdleReminderService>? logger = null) {
        _configs = [.. configs];
        _logger = logger;

        foreach (var config in _configs) {
            _turnsSinceLastUse[config.ToolName] = 0;
            _turnsSinceLastReminder[config.ToolName] = config.TurnsBetweenReminders;
        }
    }

    private static List<ToolIdleReminderConfig> CreateDefaultReminderConfigs(
        ITodoService? todoService, ITaskService? taskService) {
        return
        [
            new ToolIdleReminderConfig(
                TodoToolName.TodoWrite.ToValue(),
                turnsSinceUse: 10,
                turnsBetweenReminders: 10,
                reminderMessage: $"The {TodoToolNameEnumConstants.TodoWrite} tool hasn't been used recently. If you're business on tasks, would benefit from using the {TodoToolNameEnumConstants.TodoWrite} tool. You consider cleaning up the todo list. Make sure that you NEVER mention this reminder to the user",
                stateProvider: async ct =>
                {
                    if (todoService is null) return string.Empty;
                    var result = await todoService.ListTodosAsync(includeCompleted: true, cancellationToken: ct).ConfigureAwait(false);
                    if (!result.Success || result.TotalCount == 0) return string.Empty;
                    var items = result.Todos.Select((t, i) => $"{i + 1}. [{t.Status}] {t.Content}");
                    return $"Here are the existing contents of your todo list:\n\n[{string.Join(", ", items)}]";
                }),
            new ToolIdleReminderConfig(
                TaskToolNameEnumConstants.TaskUpdate,
                turnsSinceUse: 10,
                turnsBetweenReminders: 10,
                reminderMessage: $"The task tools haven't been used recently. If you're business on tasks, would benefit from using {TaskToolNameEnumConstants.TaskCreate} to add new tasks and {TaskToolNameEnumConstants.TaskUpdate} to update task status (set to in_progress when starting, completed and done). Consider cleaning up the task list. Make sure that you NEVER mention this reminder to the user",
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

    /// <summary>
    /// 记录一回合助手交互，更新各工具的空闲计数。
    /// </summary>
    /// <param name="toolNameUsed">本回合使用的工具名称，未使用则为 null。</param>
    public void RecordAssistantTurn(string? toolNameUsed = null) {
        foreach (var config in _configs) {
            if (string.Equals(toolNameUsed, config.ToolName, StringComparison.OrdinalIgnoreCase)) {
                _turnsSinceLastUse[config.ToolName] = 0;
            } else {
                _turnsSinceLastUse[config.ToolName]++;
            }

            _turnsSinceLastReminder[config.ToolName]++;
        }
    }

    /// <summary>
    /// 记录已向用户发送指定工具的提醒，重置其提醒间隔计数。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    public void RecordReminderSent(string toolName) {
        if (_turnsSinceLastReminder.ContainsKey(toolName)) {
            _turnsSinceLastReminder[toolName] = 0;
        }
    }

    /// <summary>
    /// 检查各工具空闲状态并生成提醒列表。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>本次生成的提醒结果列表。</returns>
    public async Task<IReadOnlyList<ToolIdleReminderResult>> CheckAndGenerateRemindersAsync(
        CancellationToken ct = default) {
        var results = new List<ToolIdleReminderResult>();

        foreach (var config in _configs) {
            var turnsSinceUse = _turnsSinceLastUse.GetValueOrDefault(config.ToolName, 0);
            var turnsSinceReminder = _turnsSinceLastReminder.GetValueOrDefault(config.ToolName, 0);

            if (turnsSinceUse < config.TurnsSinceUse || turnsSinceReminder < config.TurnsBetweenReminders) {
                continue;
            }

            string? stateContent = null;
            if (config.StateProvider is not null) {
                try {
                    stateContent = await config.StateProvider(ct).ConfigureAwait(false);
                } catch (Exception ex) {
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

    /// <summary>
    /// 重置所有工具的空闲计数与提醒间隔计数。
    /// </summary>
    public void Reset() {
        foreach (var config in _configs) {
            _turnsSinceLastUse[config.ToolName] = 0;
            _turnsSinceLastReminder[config.ToolName] = config.TurnsBetweenReminders;
        }
    }
}