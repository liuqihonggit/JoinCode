namespace JoinCode.ChatCommands;

/// <summary>
/// /bridge-kick 命令 — 断开指定 Bridge 连接
/// 用法: /bridge-kick &lt;session-id&gt;
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.BridgeKick, Description = "断开指定Bridge连接", Usage = "/bridge-kick [session-id]", Category = ChatCommandCategory.Bridge, ArgumentHint = "[session-id]")]
[ChatCommandArg("session-id", Type = "string", Description = "要断开的 Bridge 会话 ID", Required = true)]
public sealed class BridgeKickCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /bridge-kick 命令，断开指定会话的 Bridge 连接
    /// </summary>
    /// <param name="context">命令执行上下文，提供会话 ID 参数与 Bridge 客户端</param>
    /// <returns>表示命令执行结果的 <see cref="ChatCommandResult"/>，始终为 Continue</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var sessionId = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(sessionId)) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}用法: /bridge-kick <session-id>{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine("断开指定会话的Bridge连接");
            return ChatCommandResult.Continue();
        }

        var bridgeClient = context.GetCommandServices().BridgeClient;
        if (bridgeClient is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}Bridge客户端未初始化{AnsiStyleEnumConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        try {
            var state = await bridgeClient.GetStateAsync(context.CancellationToken).ConfigureAwait(false);

            if (state.ConnectionState == TransportConnectionState.Disconnected) {
                TerminalHelper.WriteLine($"{TerminalColors.Muted}Bridge连接已处于断开状态{AnsiStyleEnumConstants.Reset}");
                return ChatCommandResult.Continue();
            }

            await bridgeClient.StopAsync(context.CancellationToken).ConfigureAwait(false);

            TerminalHelper.WriteLine($"{TerminalColors.Success}已断开Bridge连接 [{sessionId}]{AnsiStyleEnumConstants.Reset}");
        } catch (Exception ex) {
            ChatCommandBase.HandleError("断开Bridge连接", ex);
        }

        return ChatCommandResult.Continue();
    }
}