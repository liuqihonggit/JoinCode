namespace JoinCode.ChatCommands;

/// <summary>
/// 精简模式切换命令 — 启用/禁用 SimpleModeService，简化提示词、减少工具集、最小化 UI、自动确认
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Simple, Description = "切换精简模式", Usage = "/simple", Category = ChatCommandCategory.Other)]
public sealed class SimpleCommand : ToggleCommandBase {
    /// <summary>命令名称</summary>
    public override string Name => ChatCommandNameEnumConstants.Simple;
    /// <summary>命令描述</summary>
    public override string Description => "切换精简模式";
    /// <summary>命令用法</summary>
    public override string Usage => "/simple";
    /// <summary>命令别名</summary>
    public override string[] Aliases => new[] { "bare" };

    /// <summary>
    /// 启用精简模式时回调
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    protected override Task OnEnabledAsync(ChatCommandContext context) {
        context.GetCommandServices().SimpleModeService?.Enable();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 禁用精简模式时回调
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    protected override Task OnDisabledAsync(ChatCommandContext context) {
        context.GetCommandServices().SimpleModeService?.Disable();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 切换精简模式状态时回调
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    protected override Task OnToggleAsync(ChatCommandContext context) {
        context.GetCommandServices().SimpleModeService?.Toggle();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 打印当前精简模式状态
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    protected override Task PrintStatusAsync(ChatCommandContext context) {
        var service = context.GetCommandServices().SimpleModeService;
        if (service is null) return Task.CompletedTask;

        if (service.IsSimpleMode) {
            var config = service.GetCurrentConfig();
            TerminalHelper.WriteLine($"{TerminalColors.Primary}精简模式已启用{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine($"  简化提示词: {(config.UseSimplePrompts ? "是" : "否")}");
            TerminalHelper.WriteLine($"  减少工具集: {(config.ReduceToolSet ? "是" : "否")}");
            TerminalHelper.WriteLine($"  最小化UI:   {(config.MinimalUI ? "是" : "否")}");
            TerminalHelper.WriteLine($"  自动确认:   {(config.AutoConfirm ? "是" : "否")}");
        } else {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}精简模式已禁用 - 使用完整模式{AnsiStyleEnumConstants.Reset}");
        }

        return Task.CompletedTask;
    }
}