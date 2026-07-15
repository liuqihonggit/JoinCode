namespace JoinCode.ChatCommands;

/// <summary>
/// /simple 命令 - 切换精简模式
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Simple, Description = "切换精简模式", Usage = "/simple", Category = ChatCommandCategory.Other)]
public sealed class SimpleCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Simple;
    public string Description => "切换精简模式";
    public string Usage => "/simple";
    public string[] Aliases => ["bare"];
    public string ArgumentHint => "[on|off]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var simpleModeService = context.Services.SimpleModeService;

        if (simpleModeService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}精简模式服务不可用{AnsiStyleConstants.Reset}");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var args = ChatCommandBase.GetNormalizedArgs(context);

        // 使用 ToggleAction 枚举处理 on/off/toggle (无参时 = toggle)
        // Status 保留为 null 分支(无 Status 显示需求, 默认 toggle)
        switch (ToggleActionExtensions.FromValue(args))
        {
            case ToggleAction.On:
                simpleModeService.Enable();
                break;
            case ToggleAction.Off:
                simpleModeService.Disable();
                break;
            case null:
                simpleModeService.Toggle();
                break;
        }

        PrintStatus(simpleModeService);

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static void PrintStatus(ISimpleModeService service)
    {
        if (service.IsSimpleMode)
        {
            var config = service.GetCurrentConfig();
            TerminalHelper.WriteLine($"{TerminalColors.Primary}精简模式已启用{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"  简化提示词: {(config.UseSimplePrompts ? "是" : "否")}");
            TerminalHelper.WriteLine($"  减少工具集: {(config.ReduceToolSet ? "是" : "否")}");
            TerminalHelper.WriteLine($"  最小化UI:   {(config.MinimalUI ? "是" : "否")}");
            TerminalHelper.WriteLine($"  自动确认:   {(config.AutoConfirm ? "是" : "否")}");
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}精简模式已禁用 - 使用完整模式{AnsiStyleConstants.Reset}");
        }
    }
}
