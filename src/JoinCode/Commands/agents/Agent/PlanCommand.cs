namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Plan, Description = "计划模式管理", Usage = "/plan [on|off|status|open] [描述]", Category = ChatCommandCategory.Agent)]
public sealed class PlanCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Plan;
    public string Description => "计划模式管理";
    public string Usage => "/plan [on|off|status|open] [描述]";
    public string[] Aliases => [];
    public string ArgumentHint => "[on|off|status|open]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context);
        var parts = ChatCommandBase.GetSplitArgs(context);
        var subCommand = parts.Length > 0 ? parts[0].ToLowerInvariant() : "toggle";

        switch (subCommand)
        {
            case PlanSubCommandConstants.On:
            case PlanSubCommandConstants.Enter:
                await EnterPlanModeAsync(context, parts);
                break;
            case PlanSubCommandConstants.Off:
            case PlanSubCommandConstants.Exit:
                await ExitPlanModeAsync(context);
                break;
            case PlanSubCommandConstants.Status:
                await ShowPlanStatusAsync(context);
                break;
                case PlanSubCommandConstants.Open:
                await OpenPlanFile(context, context.Services!.FileSystem).ConfigureAwait(false);
                break;
            case PlanSubCommandConstants.Toggle:
            default:
                await TogglePlanModeAsync(context, args);
                break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task TogglePlanModeAsync(ChatCommandContext context, string args)
    {
        var interactiveService = ResolveInteractiveService(context);
        if (interactiveService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}交互服务不可用，尝试通过 PlanService 执行{AnsiStyleConstants.Reset}");
            await FallbackExecutePlanAsync(context, args);
            return;
        }

        var status = await interactiveService.GetPlanModeStatusAsync(context.CancellationToken).ConfigureAwait(false);

        if (status.IsInPlanMode)
        {
            await ExitPlanModeAsync(context);
        }
        else
        {
            var description = string.IsNullOrWhiteSpace(args) ? null : args;
            await EnterPlanModeAsync(context, description);
        }
    }

    private static async Task EnterPlanModeAsync(ChatCommandContext context, string? description)
    {
        var interactiveService = ResolveInteractiveService(context);
        if (interactiveService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}交互服务不可用，尝试通过 PlanService 执行{AnsiStyleConstants.Reset}");
            await FallbackExecutePlanAsync(context, description ?? string.Empty);
            return;
        }

        var result = await interactiveService.EnterPlanModeAsync(
            goal: description,
            cancellationToken: context.CancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Primary}已进入计划模式{AnsiStyleConstants.Reset}");
            if (!string.IsNullOrEmpty(description))
            {
                TerminalHelper.WriteLine($"  目标: {description}");
            }
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}进入计划模式失败: {result.ErrorMessage ?? "未知错误"}{AnsiStyleConstants.Reset}");
        }
    }

    private static async Task EnterPlanModeAsync(ChatCommandContext context, string[] parts)
    {
        var description = parts.Length > 1 ? string.Join(" ", parts[1..]) : null;
        await EnterPlanModeAsync(context, description);
    }

    private static async Task ExitPlanModeAsync(ChatCommandContext context)
    {
        var interactiveService = ResolveInteractiveService(context);
        if (interactiveService is null)
        {
            TerminalHelper.WriteLine("交互服务不可用");
            return;
        }

        var result = await interactiveService.ExitPlanModeAsync(
            confirm: true,
            cancellationToken: context.CancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Primary}已退出计划模式{AnsiStyleConstants.Reset}");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}退出计划模式失败: {result.ErrorMessage ?? "未知错误"}{AnsiStyleConstants.Reset}");
        }
    }

    private static async Task ShowPlanStatusAsync(ChatCommandContext context)
    {
        var interactiveService = ResolveInteractiveService(context);
        if (interactiveService is null)
        {
            TerminalHelper.WriteLine("交互服务不可用");
            return;
        }

        var status = await interactiveService.GetPlanModeStatusAsync(context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.WriteLine("=== 计划模式状态 ===");
        TerminalHelper.WriteLine($"  模式: {(status.IsInPlanMode ? $"{TerminalColors.Primary}已开启{AnsiStyleConstants.Reset}" : "已关闭")}");

        if (status.IsInPlanMode)
        {
            if (!string.IsNullOrEmpty(status.CurrentGoal))
            {
                TerminalHelper.WriteLine($"  目标: {status.CurrentGoal}");
            }

            if (status.Steps?.Count > 0)
            {
                TerminalHelper.WriteLine($"  步骤 ({status.Steps.Count}):");
                for (var i = 0; i < status.Steps.Count; i++)
                {
                    var step = status.Steps[i];
                    var check = step.Status == TaskStatusConstants.Completed ? "✓" : "○";
                    TerminalHelper.WriteLine($"    {check} {i + 1}. {step.Description}");
                }
            }
        }
    }

    private static async Task OpenPlanFile(ChatCommandContext context, IFileSystem fs)
    {
        var planFilePath = GetPlanFilePath();
        if (planFilePath is null || !fs.FileExists(planFilePath))
        {
            TerminalHelper.WriteLine("计划文件不存在");
            TerminalHelper.WriteLine("提示: 先进入计划模式 /plan on，再使用 /plan open 编辑");
            return;
        }

        // 非交互模式(测试/管道/CI)禁止启动外部编辑器,否则会触发桌面应用弹窗
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var editor = Environment.GetEnvironmentVariable("EDITOR") ?? Environment.GetEnvironmentVariable("VISUAL") ?? "notepad";
            TerminalHelper.WriteLine($"将使用编辑器 {editor} 打开: {planFilePath}");
            return;
        }

        try
        {
            var processService = ChatCommandBase.GetService<IProcessService>(context);
            if (processService != null)
            {
                await processService.OpenAsync(planFilePath).ConfigureAwait(false);
            }
            else
            {
                var editor = Environment.GetEnvironmentVariable("EDITOR") ?? Environment.GetEnvironmentVariable("VISUAL") ?? "notepad";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = editor,
                    Arguments = planFilePath,
                    UseShellExecute = true
                });
            }
            TerminalHelper.WriteLine($"已在编辑器中打开: {planFilePath}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("打开编辑器", ex);
            TerminalHelper.WriteLine($"计划文件路径: {planFilePath}");
        }
    }

    private static async Task FallbackExecutePlanAsync(ChatCommandContext context, string description)
    {
        if (context.Services!.PlanService is null)
        {
            TerminalHelper.WriteLine("PlanService 不可用");
            return;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            TerminalHelper.WriteLine("请提供任务描述，例如: /plan 帮我创建一个TODO应用");
            return;
        }

        var result = await context.Services!.PlanService.ExecutePlanAsync(description, context.CancellationToken).ConfigureAwait(false);
        TerminalHelper.WriteLine(result);
    }

    private static IInteractiveService? ResolveInteractiveService(ChatCommandContext context)
    {
        return ChatCommandBase.GetService<IInteractiveService>(context, typeof(IInteractiveService));
    }

    private static string? GetPlanFilePath()
    {
        var appDataPath = Path.Combine(
            WorkflowConstants.Paths.JccDirectory,
            "plan.md");
        return appDataPath;
    }
}
