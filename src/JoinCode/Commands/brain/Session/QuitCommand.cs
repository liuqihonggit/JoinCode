namespace JoinCode.ChatCommands;

/// <summary>
/// /quit 命令 - 退出程序（exit 的别名）
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Quit, Description = "退出程序", Usage = "/quit", Category = ChatCommandCategory.Session, Aliases = ["q"])]
public sealed class QuitCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        return Task.FromResult(ChatCommandResult.Exit());
    }
}
