namespace JoinCode.ChatCommands;

/// <summary>
/// /exit 命令 - 退出程序
/// 对齐 TS: 确认退出对话框
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Exit, Description = "退出程序", Usage = "/exit", Category = ChatCommandCategory.Session)]
public sealed class ExitCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Exit;
    public string Description => "退出程序";
    public string Usage => "/exit";
    public string[] Aliases => ["x"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        // 对齐 TS: 退出前确认框
        // 非交互模式或测试环境直接退出（无法确认）
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return ChatCommandResult.Exit();
        }

        var confirmed = await Confirmation.ConfirmAsync("确定要退出吗？", context.CancellationToken).ConfigureAwait(false);
        return confirmed ? ChatCommandResult.Exit() : ChatCommandResult.Continue();
    }
}
