namespace JoinCode.ChatCommands;

/// <summary>
/// /proactive 命令 — 主动执行模式切换，控制 LLM 是否在用户未输入时自主推进任务。
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Proactive, Description = "主动执行模式", Usage = "/proactive [on|off|pause|resume|status]", Category = ChatCommandCategory.Task)]
[ChatCommandArg("action", Type = "string", Description = "主动模式动作(别名: on=activate/1, off=deactivate/0, status=s, pause=p, resume=r),省略时等同 status", Default = "status", Enum = new[] { "on", "off", "pause", "resume", "status" })]
public sealed class ProactiveCommand : ToggleCommandBase
{
    /// <summary>命令名称。</summary>
    public override string Name => ChatCommandNameEnumConstants.Proactive;
    /// <summary>命令描述。</summary>
    public override string Description => "主动执行模式";
    /// <summary>命令用法提示。</summary>
    public override string Usage => "/proactive [on|off|pause|resume|status]";
    /// <summary>是否隐藏命令（不在帮助列表中显示）。</summary>
    public override bool IsHidden => true;
    /// <summary>参数提示文本。</summary>
    protected override string ArgumentHintText => "[on|off|pause|resume|status]";

    /// <summary>
    /// 将字符串参数解析为切换动作。
    /// </summary>
    /// <param name="args">用户输入的参数字符串。</param>
    /// <returns>匹配到的切换动作，无法识别时返回 null。</returns>
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

    /// <summary>参数为空时的默认动作。</summary>
    protected override ToggleNullAction NullAction => ToggleNullAction.Status;

    /// <summary>
    /// 启用主动模式时的处理逻辑。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected override Task OnEnabledAsync(ChatCommandContext context)
    {
        var proactiveService = GetService<IProactiveStateService>(context);
        proactiveService?.Activate("user-command");
        TerminalHelper.WriteLine("主动模式已激活");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用主动模式时的处理逻辑。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected override Task OnDisabledAsync(ChatCommandContext context)
    {
        var proactiveService = GetService<IProactiveStateService>(context);
        proactiveService?.Deactivate();
        TerminalHelper.WriteLine("主动模式已停用");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 默认动作处理（pause/resume 等非开关动作）。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <param name="args">用户输入的参数字符串。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected override async Task OnDefaultAsync(ChatCommandContext context, string args)
    {
        var proactiveService = GetService<IProactiveStateService>(context);
        if (proactiveService is null) return;

        var lower = args.ToLowerInvariant();
        switch (lower)
        {
            case ResumeLifecycleEnumConstants.Pause:
            case "p":
                proactiveService.Pause();
                TerminalHelper.WriteLine("主动模式已暂停");
                break;
            case ResumeLifecycleEnumConstants.Resume:
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

    /// <summary>
    /// 打印主动模式当前状态。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
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
