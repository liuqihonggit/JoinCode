namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.BridgeKick, Description = "断开指定Bridge连接", Usage = "/bridge-kick [session-id]", Category = ChatCommandCategory.Bridge)]
public sealed class BridgeKickCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.BridgeKick;
    public string Description => "断开指定Bridge连接";
    public string Usage => "/bridge-kick [session-id]";
    public string[] Aliases => [];
    public string ArgumentHint => "[session-id]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var sessionId = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(sessionId))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}用法: /bridge-kick <session-id>{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine("断开指定会话的Bridge连接");
            return ChatCommandResult.Continue();
        }

        var bridgeClient = context.Services!.BridgeClient;
        if (bridgeClient is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}Bridge客户端未初始化{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        try
        {
            var state = await bridgeClient.GetStateAsync(context.CancellationToken);

            if (state.ConnectionState == TransportConnectionState.Disconnected)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Muted}Bridge连接已处于断开状态{AnsiStyleConstants.Reset}");
                return ChatCommandResult.Continue();
            }

            await bridgeClient.StopAsync(context.CancellationToken);

            TerminalHelper.WriteLine($"{TerminalColors.Success}已断开Bridge连接 [{sessionId}]{AnsiStyleConstants.Reset}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("断开Bridge连接", ex);
        }

        return ChatCommandResult.Continue();
    }
}