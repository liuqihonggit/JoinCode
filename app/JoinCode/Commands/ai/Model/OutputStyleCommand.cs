
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.OutputStyle, Description = "已废弃: 请使用 /config 更改输出样式", Usage = "/output-style", Category = ChatCommandCategory.Model, IsHidden = true)]
public sealed class OutputStyleCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        TerminalHelper.WriteLine("/output-style 已废弃。请使用 /config 更改输出样式，或在设置文件中设置。更改将在下次会话生效。");
        return Task.FromResult(ChatCommandResult.Continue());
    }
}
