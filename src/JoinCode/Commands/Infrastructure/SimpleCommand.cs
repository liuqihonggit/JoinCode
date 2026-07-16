namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Simple, Description = "切换精简模式", Usage = "/simple", Category = ChatCommandCategory.Other)]
public sealed class SimpleCommand : ToggleCommandBase
{
    public override string Name => ChatCommandNameConstants.Simple;
    public override string Description => "切换精简模式";
    public override string Usage => "/simple";
    public override string[] Aliases => ["bare"];

    protected override void OnEnabled(ChatCommandContext context)
    {
        var service = context.Services.SimpleModeService;
        service?.Enable();
    }

    protected override void OnDisabled(ChatCommandContext context)
    {
        var service = context.Services.SimpleModeService;
        service?.Disable();
    }

    protected override void OnToggle(ChatCommandContext context)
    {
        var service = context.Services.SimpleModeService;
        service?.Toggle();
    }

    protected override void PrintStatus(ChatCommandContext context)
    {
        var service = context.Services.SimpleModeService;
        if (service is null) return;

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
