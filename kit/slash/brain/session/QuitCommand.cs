namespace JoinCode.ChatCommands;

/// <summary>
/// /quit 命令 - 退出程序（exit 的别名）
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Quit, Description = "退出程序", Usage = "/quit", Category = ChatCommandCategory.Session, Aliases = ["q"])]
public sealed class QuitCommand : ChatCommandBase
{
    /// <summary>
    /// 执行 /quit 命令，直接返回退出结果。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务，承载退出命令结果。</returns>
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        return Task.FromResult(ChatCommandResult.Exit());
    }
}
