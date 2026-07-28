namespace JoinCode.ChatCommands;

public sealed class CustomChatCommand : IChatCommand
{
    private readonly CustomCommand _command;

    public string Name => _command.FullName;
    public string Description => string.IsNullOrEmpty(_command.Description) ? $"自定义命令: {_command.FullName}" : _command.Description;
    public string Usage => $"/{_command.FullName} <参数>";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public CustomCommand Command => _command;

    public CustomChatCommand(CustomCommand command)
    {
        _command = command;
    }

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var arguments = ChatCommandBase.GetNormalizedArgs(context);
        var prompt = _command.ApplyArguments(arguments);

        if (_command.DisableModelInvocation)
        {
            TerminalHelper.WriteLine(prompt);
            return ChatCommandResult.Continue();
        }

        try
        {
            var result = await context.Services.ChatService.SendMessageAsync(prompt).ConfigureAwait(false);
            TerminalHelper.WriteLine(result);
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("自定义命令执行", ex);
        }

        return ChatCommandResult.Continue();
    }
}
