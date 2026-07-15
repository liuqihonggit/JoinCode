
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Goal, Description = "目标自主循环引擎（持续工作直到条件满足）", Usage = "/goal <目标描述> [--constraint '约束'] [--budget <token数>] | /goal | /goal pause | /goal resume | /goal clear | /goal --cron <表达式> <描述>", Category = ChatCommandCategory.Task)]
public sealed partial class GoalCommand : IChatCommand
{
    [Inject] private readonly ILogger<GoalCommand>? _logger;

    public string Name => ChatCommandNameConstants.Goal;
    public string Description => "目标自主循环引擎（持续工作直到条件满足）";
    public string Usage => "/goal <目标描述> [--constraint '约束'] [--budget <token数>] | /goal | /goal pause | /goal resume | /goal clear | /goal --cron <表达式> <描述>";
    public string[] Aliases => [];
    public string ArgumentHint => "<目标描述|子命令>";
    public bool IsHidden => false;

    public GoalCommand(ILogger<GoalCommand>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var goalEngine = context.Services.GoalEngine;
        if (goalEngine is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}错误: 目标引擎未注册{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(args))
        {
            ShowStatus(goalEngine);
            return ChatCommandResult.Continue();
        }

        var parts = args.Split(' ', 2);
        var subCommand = parts[0].ToLowerInvariant();

        switch (subCommand)
        {
            case ResumeLifecycleConstants.Pause:
                await goalEngine.PauseAsync(context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Warning}◎ /goal 已暂停{AnsiStyleConstants.Reset}");
                break;

            case ResumeLifecycleConstants.Resume:
                await goalEngine.ResumeAsync(context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Success}◎ /goal 已恢复{AnsiStyleConstants.Reset}");
                break;

            case ResumeLifecycleConstants.Clear:
            case ResumeLifecycleConstants.Stop:
            case ResumeLifecycleConstants.Off:
            case ResumeLifecycleConstants.Reset:
            case ResumeLifecycleConstants.Cancel:
                await goalEngine.ClearAsync(context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine("目标已清除");
                break;

            default:
                var parsed = ParseGoalArgs(args);
                if (parsed.IsCron)
                {
                    await ExecuteCronGoalAsync(goalEngine, context, parsed).ConfigureAwait(false);
                }
                else
                {
                    await ExecuteGoalAsync(goalEngine, context, parsed).ConfigureAwait(false);
                }
                break;
        }

        return ChatCommandResult.Continue();
    }

    private async Task ExecuteGoalAsync(IGoalEngine goalEngine, ChatCommandContext context, GoalParseResult parsed)
    {
        _logger?.LogInformation("启动目标引擎: {Objective} (约束: {Constraints}, 预算: {Budget})",
            parsed.Objective, parsed.Constraints.Count, parsed.TokenBudget?.ToString() ?? "无限制");

        TerminalHelper.WriteLine($"{TerminalColors.Info}◎ /goal active{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  目标: {parsed.Objective}");

        if (parsed.Constraints.Count > 0)
        {
            TerminalHelper.WriteLine($"  约束: {string.Join(", ", parsed.Constraints)}");
        }

        if (parsed.TokenBudget.HasValue)
        {
            TerminalHelper.WriteLine($"  预算: {parsed.TokenBudget.Value} Token");
        }

        try
        {
            var state = await goalEngine.StartAsync(
                parsed.Objective,
                parsed.Constraints,
                parsed.TokenBudget,
                context.CancellationToken).ConfigureAwait(false);
            ShowGoalState(state);
        }
        catch (InvalidOperationException ex)
        {
            ChatCommandBase.HandleError("目标执行", ex);
        }
    }

    private async Task ExecuteCronGoalAsync(IGoalEngine goalEngine, ChatCommandContext context, GoalParseResult parsed)
    {
        if (string.IsNullOrWhiteSpace(parsed.CronExpression))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}错误: 定时模式需要指定 Cron 表达式{AnsiStyleConstants.Reset}");
            return;
        }

        var cronTaskStore = context.Services.CronTaskStore;
        if (cronTaskStore is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}错误: Cron 任务存储未注册{AnsiStyleConstants.Reset}");
            return;
        }

        var request = new CreateCronTaskRequest
        {
            CronExpression = parsed.CronExpression,
            Prompt = parsed.Objective,
            IsRecurring = true,
            IsDurable = true
        };

        var cronTask = await cronTaskStore.AddTaskAsync(request, context.CancellationToken).ConfigureAwait(false);
        TerminalHelper.WriteLine($"{TerminalColors.Success}定时目标已注册{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  Cron: {parsed.CronExpression}");
        TerminalHelper.WriteLine($"  目标: {parsed.Objective}");
        TerminalHelper.WriteLine($"  任务ID: {cronTask.Id}");
    }

    internal static GoalParseResult ParseGoalArgs(string args)
    {
        if (args.StartsWith("--cron ", StringComparison.OrdinalIgnoreCase) ||
            args.StartsWith("-c ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = args.StartsWith("--cron ", StringComparison.OrdinalIgnoreCase)
                ? args["--cron ".Length..]
                : args["-c ".Length..];

            var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 5)
            {
                return new GoalParseResult(string.Empty, [], null, null, true);
            }

            var cronExpr = string.Join(' ', tokens[..5]);
            var desc = tokens.Length > 5 ? string.Join(' ', tokens[5..]) : string.Empty;
            return new GoalParseResult(desc.Trim(), [], null, cronExpr, true);
        }

        var constraints = new List<string>();
        int? tokenBudget = null;
        var remaining = args;

        while (true)
        {
            if (remaining.StartsWith("--constraint ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = remaining["--constraint ".Length..];
                var constraint = ExtractQuotedOrWord(rest, out var afterConstraint);
                if (!string.IsNullOrWhiteSpace(constraint))
                {
                    constraints.Add(constraint);
                }
                remaining = afterConstraint.TrimStart();
            }
            else if (remaining.StartsWith("--budget ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = remaining["--budget ".Length..];
                var budgetStr = ExtractQuotedOrWord(rest, out var afterBudget);
                if (int.TryParse(budgetStr, out var budget))
                {
                    tokenBudget = budget;
                }
                remaining = afterBudget.TrimStart();
            }
            else
            {
                break;
            }
        }

        return new GoalParseResult(remaining.Trim(), constraints, tokenBudget, null, false);
    }

    private static string ExtractQuotedOrWord(string input, out string remaining)
    {
        if (string.IsNullOrEmpty(input))
        {
            remaining = string.Empty;
            return string.Empty;
        }

        if (input[0] is '\'' or '"')
        {
            var quote = input[0];
            var endIdx = input.IndexOf(quote, 1);
            if (endIdx > 0)
            {
                remaining = input[(endIdx + 1)..];
                return input[1..endIdx];
            }
        }

        var spaceIdx = input.IndexOf(' ');
        if (spaceIdx > 0)
        {
            remaining = input[(spaceIdx + 1)..];
            return input[..spaceIdx];
        }

        remaining = string.Empty;
        return input;
    }

    private static void ShowStatus(IGoalEngine goalEngine)
    {
        var state = goalEngine.CurrentState;
        if (state is null)
        {
            TerminalHelper.WriteLine("当前没有活跃目标");
            return;
        }

        ShowGoalState(state);
    }

    private static void ShowGoalState(GoalState state)
    {
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Info}◎ /goal {FormatStatus(state.Status)}{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  目标: {state.Objective}");
        TerminalHelper.WriteLine($"  ID: {state.GoalId}");
        TerminalHelper.WriteLine($"  状态: {FormatStatus(state.Status)}");
        TerminalHelper.WriteLine($"  轮数: {state.TurnsCompleted}");
        TerminalHelper.WriteLine($"  Token: {state.TokensUsed}{(state.TokenBudget.HasValue ? $" / {state.TokenBudget.Value}" : "")}");
        TerminalHelper.WriteLine($"  已用时间: {state.Elapsed:hh\\:mm\\:ss}");

        if (state.Constraints.Count > 0)
        {
            TerminalHelper.WriteLine($"  约束: {string.Join(", ", state.Constraints)}");
        }

        if (state.LastEvaluation is not null)
        {
            var evalColor = state.LastEvaluation.IsCompleted ? TerminalColors.Success : TerminalColors.Warning;
            TerminalHelper.WriteLine($"  评估器: {evalColor}{state.LastEvaluation.Reason}{AnsiStyleConstants.Reset}");
        }

        TerminalHelper.NewLine();
    }

    private static string FormatStatus(GoalStatus status) => status switch
    {
        GoalStatus.Pursuing => $"{TerminalColors.Success}运行中{AnsiStyleConstants.Reset}",
        GoalStatus.Paused => $"{TerminalColors.Warning}已暂停{AnsiStyleConstants.Reset}",
        GoalStatus.Achieved => $"{TerminalColors.Success}已完成{AnsiStyleConstants.Reset}",
        GoalStatus.Unmet => $"{TerminalColors.Error}未完成{AnsiStyleConstants.Reset}",
        GoalStatus.BudgetLimited => $"{TerminalColors.Warning}预算耗尽{AnsiStyleConstants.Reset}",
        _ => status.ToString()
    };
}

internal sealed record GoalParseResult(
    string Objective,
    List<string> Constraints,
    int? TokenBudget,
    string? CronExpression,
    bool IsCron);
