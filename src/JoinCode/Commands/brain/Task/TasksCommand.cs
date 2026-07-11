namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Tasks, Description = "列出和管理后台任务", Usage = "/tasks [kill|detail|create|update|complete|todo]", Category = ChatCommandCategory.Task)]
public sealed class TasksCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Tasks;
    public string Description => "列出和管理后台任务";
    public string Usage => "/tasks [kill|detail|create|update|complete|todo]";
    public string[] Aliases => ["task", "bashes"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : null;

        if (action is null)
        {
            await ListBackgroundTasksAsync(context).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        switch (action)
        {
            case TasksActionConstants.Kill:
                await KillTaskAsync(context, args).ConfigureAwait(false);
                break;
            case TasksActionConstants.Detail:
                await ShowTaskDetailAsync(context, args).ConfigureAwait(false);
                break;
            case CrudActionConstants.Create:
            case CrudActionConstants.New:
                await CreateTaskAsync(context, args).ConfigureAwait(false);
                break;
            case CrudActionConstants.Update:
                await UpdateTaskAsync(context, args).ConfigureAwait(false);
                break;
            case TasksActionConstants.Complete:
                await CompleteTaskAsync(context, args).ConfigureAwait(false);
                break;
            case TasksActionConstants.Todo:
                await ListTodosAsync(context, args).ConfigureAwait(false);
                break;
            default:
                TerminalHelper.WriteLine($"{TerminalColors.Error}未知操作: {action}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine("可用操作: kill, detail, create, update, complete, todo");
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task ListBackgroundTasksAsync(ChatCommandContext context)
    {
        var allTasks = new List<(string Id, string Type, string Description, string Status)>();

        var shellService = ChatCommandBase.GetService<IShellBackgroundTaskService>(context, typeof(IShellBackgroundTaskService));
        if (shellService is not null)
        {
            var tasks = await shellService.ListTasksAsync(context.CancellationToken).ConfigureAwait(false);
            var running = tasks.Where(t => t.Status == TaskExecutionStatus.Running || t.Status == TaskExecutionStatus.Pending).ToList();
            foreach (var t in running)
            {
                var status = t.Status == TaskExecutionStatus.Running ? "运行中" : "等待中";
                allTasks.Add((t.TaskId, "Shell", t.Command, status));
            }
        }

        var agentCoordinator = ChatCommandBase.GetService<IAgentCoordinator>(context, typeof(IAgentCoordinator));
        if (agentCoordinator is not null)
        {
            var agents = await agentCoordinator.GetRunningAgentsAsync(context.CancellationToken).ConfigureAwait(false);
            foreach (var a in agents)
            {
                allTasks.Add((a.Id, "Agent", a.DisplayName ?? a.AgentType ?? "Unknown", "运行中"));
            }
        }

        if (allTasks.Count == 0)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}没有正在运行的后台任务{AnsiStyleConstants.Reset}");
        }
        else if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            // 交互模式：PaginatedList
            var list = new PaginatedList<(string Id, string Type, string Description, string Status)>(
                "后台任务",
                allTasks.ToArray(),
                t => $"  [{t.Type}] {t.Id} — {t.Description} ({t.Status})",
                pageSize: 10);

            await list.ShowAsync(context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            // 非交互模式：纯文本
            foreach (var t in allTasks)
            {
                var statusColor = t.Status == "运行中" ? TerminalColors.Warning : TerminalColors.Muted;
                TerminalHelper.WriteLine($"{statusColor}● [{t.Type}] {t.Id} — {t.Description}{AnsiStyleConstants.Reset}");
            }
        }

        TerminalHelper.WriteLine($"{TerminalColors.Muted}使用 /tasks kill <id> 停止任务, /tasks detail <id> 查看详情{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"{TerminalColors.Muted}任务管理: /tasks create|update|complete|todo{AnsiStyleConstants.Reset}");
    }

    private static async Task KillTaskAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /tasks kill <id>{AnsiStyleConstants.Reset}");
            return;
        }

        var taskId = args[1];

        var shellService = ChatCommandBase.GetService<IShellBackgroundTaskService>(context, typeof(IShellBackgroundTaskService));
        if (shellService is not null)
        {
            var cancelled = await shellService.CancelTaskAsync(taskId, context.CancellationToken).ConfigureAwait(false);
            if (cancelled)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Success}已停止 Shell 任务: {taskId}{AnsiStyleConstants.Reset}");
                return;
            }
        }

        var agentCoordinator = ChatCommandBase.GetService<IAgentCoordinator>(context, typeof(IAgentCoordinator));
        if (agentCoordinator is not null)
        {
            var stopped = await agentCoordinator.StopAgentAsync(taskId, context.CancellationToken).ConfigureAwait(false);
            if (stopped)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Success}已停止 Agent: {taskId}{AnsiStyleConstants.Reset}");
                return;
            }
        }

        var taskService = context.Services!.TaskService;
        if (taskService is not null)
        {
            var result = await taskService.StopTaskAsync(taskId, reason: "Killed by /tasks kill", cancellationToken: context.CancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Success}已停止任务: {taskId}{AnsiStyleConstants.Reset}");
                return;
            }
        }

        TerminalHelper.WriteLine($"{TerminalColors.Error}未找到任务: {taskId}{AnsiStyleConstants.Reset}");
    }

    private static async Task ShowTaskDetailAsync(ChatCommandContext context, string[] args)
    {
        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /tasks detail <id>{AnsiStyleConstants.Reset}");
            return;
        }

        var taskId = args[1];

        var shellService = ChatCommandBase.GetService<IShellBackgroundTaskService>(context, typeof(IShellBackgroundTaskService));
        if (shellService is not null)
        {
            var task = await shellService.GetTaskAsync(taskId, context.CancellationToken).ConfigureAwait(false);
            if (task is not null)
            {
                TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}── Shell 任务详情 ──{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine($"  ID: {task.TaskId}");
                TerminalHelper.WriteLine($"  命令: {task.Command}");
                TerminalHelper.WriteLine($"  状态: {task.Status}");
                if (task.ExitCode.HasValue)
                    TerminalHelper.WriteLine($"  退出码: {task.ExitCode}");
                if (task.AgentId is not null)
                    TerminalHelper.WriteLine($"  Agent: {task.AgentId}");

                var output = await shellService.GetTaskOutputAsync(taskId, context.CancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(output))
                {
                    TerminalHelper.WriteLine($"{TerminalColors.Muted}── 输出 ──{AnsiStyleConstants.Reset}");
                    var lines = output.Split('\n');
                    var displayLines = lines.Length > 30 ? lines[^30..] : lines;
                    foreach (var line in displayLines)
                        TerminalHelper.WriteLine($"  {line}");
                    if (lines.Length > 30)
                        TerminalHelper.WriteLine($"  {TerminalColors.Muted}... (显示最后 30 行){AnsiStyleConstants.Reset}");
                }

                return;
            }
        }

        var taskService = context.Services!.TaskService;
        if (taskService is not null)
        {
            var task = await taskService.GetTaskAsync(taskId, context.CancellationToken).ConfigureAwait(false);
            if (task is not null)
            {
                TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}── 任务详情 ──{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine($"  ID: {task.Id}");
                TerminalHelper.WriteLine($"  标题: {task.Title}");
                TerminalHelper.WriteLine($"  状态: {task.Status}");
                TerminalHelper.WriteLine($"  优先级: {task.Priority}");
                if (task.Assignee is not null)
                    TerminalHelper.WriteLine($"  负责人: {task.Assignee}");
                return;
            }
        }

        TerminalHelper.WriteLine($"{TerminalColors.Error}未找到任务: {taskId}{AnsiStyleConstants.Reset}");
    }

    private static async Task CreateTaskAsync(ChatCommandContext context, string[] args)
    {
        var taskService = context.Services!.TaskService;
        if (taskService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}任务服务不可用{AnsiStyleConstants.Reset}");
            return;
        }

        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /tasks create <标题> [--priority <high|medium|low>] [--assignee <负责人>]{AnsiStyleConstants.Reset}");
            return;
        }

        var title = string.Join(" ", args[1..]);
        var priority = "medium";
        string? assignee = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--priority" && i + 1 < args.Length)
            {
                priority = args[i + 1];
                i++;
            }
            else if (args[i] == "--assignee" && i + 1 < args.Length)
            {
                assignee = args[i + 1];
                i++;
            }
        }

        title = title.Replace($"--priority {priority}", "").Replace($"--assignee {assignee}", "").Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}标题不能为空{AnsiStyleConstants.Reset}");
            return;
        }

        var result = await taskService.CreateTaskAsync(
            title: title,
            description: null,
            assignee: assignee,
            dueDate: null,
            priority: priority,
            tags: null,
            cancellationToken: context.CancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Success}创建任务成功{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"  ID: {result.Task?.Id}");
            TerminalHelper.WriteLine($"  标题: {result.Task?.Title}");
            TerminalHelper.WriteLine($"  优先级: {priority}");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}创建任务失败: {result.ErrorMessage}{AnsiStyleConstants.Reset}");
        }
    }

    private static async Task UpdateTaskAsync(ChatCommandContext context, string[] args)
    {
        var taskService = context.Services!.TaskService;
        if (taskService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}任务服务不可用{AnsiStyleConstants.Reset}");
            return;
        }

        if (args.Length < 3)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /tasks update <id> [--title <新标题>] [--status <新状态>] [--priority <新优先级>]{AnsiStyleConstants.Reset}");
            return;
        }

        var taskId = args[1];
        string? newTitle = null;
        string? newStatus = null;
        string? newPriority = null;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--title" && i + 1 < args.Length)
            {
                newTitle = args[i + 1];
                i++;
            }
            else if (args[i] == "--status" && i + 1 < args.Length)
            {
                newStatus = args[i + 1];
                i++;
            }
            else if (args[i] == "--priority" && i + 1 < args.Length)
            {
                newPriority = args[i + 1];
                i++;
            }
        }

        var result = await taskService.UpdateTaskAsync(
            new UpdateTaskRequest
            {
                TaskId = taskId,
                Title = newTitle,
                Status = newStatus,
                Priority = newPriority,
            },
            cancellationToken: context.CancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Success}更新任务 {taskId} 成功{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}更新任务失败: {result.ErrorMessage}{AnsiStyleConstants.Reset}");
        }
    }

    private static async Task CompleteTaskAsync(ChatCommandContext context, string[] args)
    {
        var taskService = context.Services!.TaskService;
        if (taskService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}任务服务不可用{AnsiStyleConstants.Reset}");
            return;
        }

        if (args.Length < 2)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}用法: /tasks complete <id>{AnsiStyleConstants.Reset}");
            return;
        }

        var taskId = args[1];

        var result = await taskService.UpdateTaskAsync(
            new UpdateTaskRequest
            {
                TaskId = taskId,
                Status = TodoStatus.Completed.ToValue(),
            },
            cancellationToken: context.CancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Success}任务 {taskId} 已完成{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}完成任务失败: {result.ErrorMessage}{AnsiStyleConstants.Reset}");
        }
    }

    private static async Task ListTodosAsync(ChatCommandContext context, string[] args)
    {
        var todoService = context.Services!.TodoService;
        if (todoService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}待办服务不可用{AnsiStyleConstants.Reset}");
            return;
        }

        TerminalHelper.WriteLine("=== 待办事项 ===\n");

        var result = await todoService.ListTodosAsync(
            status: null,
            priority: null,
            includeCompleted: true,
            cancellationToken: context.CancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}获取待办列表失败{AnsiStyleConstants.Reset}");
            return;
        }

        if (result.Todos.Count == 0)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}暂无待办事项{AnsiStyleConstants.Reset}");
            return;
        }

        foreach (var todo in result.Todos)
        {
            var todoStatus = TodoStatusExtensions.FromValue(todo.Status);
            var statusIcon = todoStatus switch
            {
                TodoStatus.Completed => "✓",
                TodoStatus.InProgress => "◐",
                _ => "○"
            };
            var statusColor = todoStatus switch
            {
                TodoStatus.Completed => TerminalColors.Success,
                TodoStatus.InProgress => TerminalColors.Warning,
                _ => TerminalColors.Muted
            };

            TerminalHelper.WriteLine($"{statusColor}{statusIcon} [{todo.Id}] {todo.Content}{AnsiStyleConstants.Reset}");
            if (todo.Priority is not null)
                TerminalHelper.WriteLine($"    优先级: {todo.Priority}");
        }

        TerminalHelper.WriteLine($"\n{TerminalColors.Muted}共 {result.TotalCount} 项, 待处理 {result.PendingCount}, 已完成 {result.CompletedCount}{AnsiStyleConstants.Reset}");
    }
}
