
namespace JoinCode.ChatCommands;

/// <summary>
/// /history 命令 - 查看聊天历史
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.History, Description = "查看聊天历史", Usage = "/history", Category = ChatCommandCategory.Session, Aliases = ["hist"], ExposeToMcp = true)]
public sealed class HistoryCommand : ChatCommandBase
{
    /// <summary>
    /// 执行 /history 命令，输出当前会话的聊天历史消息列表。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务，承载命令执行结果。</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        await DisplayMessageListAsync(context.GetCommandServices().ChatService).ConfigureAwait(false);
        return ChatCommandResult.Continue();
    }

    private static async Task DisplayMessageListAsync(IChatService chatService)
    {
        var history = await chatService.GetMessageListAsync().ConfigureAwait(false);
        TerminalHelper.WriteLine("=== 聊天历史 ===");

        if (history is null || history.Count == 0)
        {
            TerminalHelper.WriteLine("(暂无聊天历史)");
            return;
        }

        foreach (var message in history)
        {
            TerminalHelper.WriteLine($"[{message.Role}]: {message.Content}");
        }
    }
}
