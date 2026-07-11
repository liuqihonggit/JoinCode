namespace JoinCode.ChatCommands;

/// <summary>
/// /quit 命令 - 退出程序（exit 的别名）
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Quit, Description = "退出程序", Usage = "/quit", Category = ChatCommandCategory.Session)]
public sealed class QuitCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Quit;
    public string Description => "退出程序";
    public string Usage => "/quit";
    public string[] Aliases => ["q"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        return Task.FromResult(ChatCommandResult.Exit());
    }
}
