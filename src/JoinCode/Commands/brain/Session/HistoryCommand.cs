
namespace JoinCode.ChatCommands;

/// <summary>
/// /history 命令 - 查看聊天历史
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.History, Description = "查看聊天历史", Usage = "/history", Category = ChatCommandCategory.Session, Aliases = ["hist"])]
public sealed class HistoryCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        await DisplayMessageListAsync(context.Services.ChatService).ConfigureAwait(false);
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
