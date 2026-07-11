namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Proactive, Description = "主动执行模式", Usage = "/proactive [on|off|pause|resume|status]", Category = ChatCommandCategory.Task)]
public sealed class ProactiveCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Proactive;
    public string Description => "主动执行模式";
    public string Usage => "/proactive [on|off|pause|resume|status]";
    public string[] Aliases => [];
    public string ArgumentHint => "[on|off|pause|resume|status]";
    public bool IsHidden => true;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var proactiveService = ChatCommandBase.GetService<IProactiveStateService>(context);

        if (proactiveService is null)
            return Task.FromResult(ChatCommandResult.Continue());

        var arg = context.Arguments?.Trim().ToLowerInvariant();

        // 主开关动作: 使用 ToggleAction 枚举(含别名 activate/1, deactivate/0, s)
        ToggleAction? toggle = arg switch
        {
            "on" or "activate" or "1" => ToggleAction.On,
            "off" or "deactivate" or "0" => ToggleAction.Off,
            "status" or "s" or null or "" => ToggleAction.Status,
            _ => null
        };

        if (toggle is not null)
        {
            switch (toggle.Value)
            {
                case ToggleAction.On:
                    proactiveService.Activate("user-command");
                    TerminalHelper.WriteLine("主动模式已激活");
                    return Task.FromResult(ChatCommandResult.Continue());
                case ToggleAction.Off:
                    proactiveService.Deactivate();
                    TerminalHelper.WriteLine("主动模式已停用");
                    return Task.FromResult(ChatCommandResult.Continue());
                case ToggleAction.Status:
                    HandleStatus(proactiveService);
                    return Task.FromResult(ChatCommandResult.Continue());
            }
        }

        // 额外动作 (pause/resume) — 不属于 ToggleAction 范围,使用 ResumeLifecycle 枚举
        switch (arg)
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

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static void HandleStatus(IProactiveStateService proactiveService)
    {
        TerminalHelper.WriteLine("主动执行模式:");
        TerminalHelper.WriteLine($"  激活: {(proactiveService.IsActive ? "是" : "否")}");
        TerminalHelper.WriteLine($"  暂停: {(proactiveService.IsPaused ? "是" : "否")}");
        TerminalHelper.WriteLine($"  上下文阻塞: {(proactiveService.IsContextBlocked ? "是" : "否")}");
    }
}
