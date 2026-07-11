
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.OutputStyle, Description = "已废弃: 请使用 /config 更改输出样式", Usage = "/output-style", Category = ChatCommandCategory.Model)]
public sealed class OutputStyleCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.OutputStyle;
    public string Description => "已废弃: 请使用 /config 更改输出样式";
    public string Usage => "/output-style";
    public string[] Aliases => [];
    public string ArgumentHint => "";
    public bool IsHidden => true;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        TerminalHelper.WriteLine("/output-style 已废弃。请使用 /config 更改输出样式，或在设置文件中设置。更改将在下次会话生效。");
        return Task.FromResult(ChatCommandResult.Continue());
    }
}
