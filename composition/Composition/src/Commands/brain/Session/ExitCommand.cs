namespace JoinCode.ChatCommands;

/// <summary>
/// /exit 命令 - 退出程序
/// 对齐 TS: 确认退出对话框
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Exit, Description = "退出程序", Usage = "/exit", Category = ChatCommandCategory.Session, Aliases = ["x"])]
public sealed class ExitCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        // T9：UI 注入确认回调优先（GUI 弹窗/TUI 对话框），回退 CLI 终端 y/N。
        // 非交互环境且无注入回调时直接退出（测试/PTY 场景，无法确认）
        if (context.Confirm is { } confirm)
        {
            return confirm("确定要退出吗？") ? ChatCommandResult.Exit() : ChatCommandResult.Continue();
        }

        // 非交互模式或测试环境直接退出（无法确认）
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return ChatCommandResult.Exit();
        }

        var confirmed = await Confirmation.ConfirmAsync("确定要退出吗？", context.CancellationToken).ConfigureAwait(false);
        return confirmed ? ChatCommandResult.Exit() : ChatCommandResult.Continue();
    }
}
