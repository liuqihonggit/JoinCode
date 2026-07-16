namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Proactive, Description = "主动执行模式", Usage = "/proactive [on|off|pause|resume|status]", Category = ChatCommandCategory.Task)]
public sealed class ProactiveCommand : ToggleCommandBase
{
    public override string Name => ChatCommandNameConstants.Proactive;
    public override string Description => "主动执行模式";
    public override string Usage => "/proactive [on|off|pause|resume|status]";
    public override bool IsHidden => true;
    protected override string ArgumentHintText => "[on|off|pause|resume|status]";

    protected override ToggleAction? ResolveToggleAction(string args)
    {
        var lower = args.ToLowerInvariant();
        return lower switch
        {
            "on" or "activate" or "1" => ToggleAction.On,
            "off" or "deactivate" or "0" => ToggleAction.Off,
            "status" or "s" or "" => ToggleAction.Status,
            null => ToggleAction.Status,
            _ => null,
        };
    }

    protected override ToggleNullAction NullAction => ToggleNullAction.Status;

    protected override Task OnEnabledAsync(ChatCommandContext context)
    {
        var proactiveService = GetService<IProactiveStateService>(context);
        proactiveService?.Activate("user-command");
        TerminalHelper.WriteLine("主动模式已激活");
        return Task.CompletedTask;
    }

    protected override Task OnDisabledAsync(ChatCommandContext context)
    {
        var proactiveService = GetService<IProactiveStateService>(context);
        proactiveService?.Deactivate();
        TerminalHelper.WriteLine("主动模式已停用");
        return Task.CompletedTask;
    }

    protected override async Task OnDefaultAsync(ChatCommandContext context, string args)
    {
        var proactiveService = GetService<IProactiveStateService>(context);
        if (proactiveService is null) return;

        var lower = args.ToLowerInvariant();
        switch (lower)
        {
            case ResumeLifecycleConstants.Pause:
            case "p":
                proactiveService.Pause();
                TerminalHelper.WriteLine("主动模式已暂停");
                break;
            case ResumeLifecycleConstants.Resume:
            case "r":
                proactiveService.Resume();
                TerminalHelper.WriteLine("主动模式已恢复");
                break;
            default:
                TerminalHelper.WriteLine($"未知参数: {context.Arguments}");
                TerminalHelper.WriteLine("用法: /proactive [on|off|pause|resume|status]");
                break;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    protected override Task PrintStatusAsync(ChatCommandContext context)
    {
        var proactiveService = GetService<IProactiveStateService>(context);
        if (proactiveService is null) return Task.CompletedTask;

        TerminalHelper.WriteLine("主动执行模式:");
        TerminalHelper.WriteLine($"  激活: {(proactiveService.IsActive ? "是" : "否")}");
        TerminalHelper.WriteLine($"  暂停: {(proactiveService.IsPaused ? "是" : "否")}");
        TerminalHelper.WriteLine($"  上下文阻塞: {(proactiveService.IsContextBlocked ? "是" : "否")}");

        return Task.CompletedTask;
    }
}
