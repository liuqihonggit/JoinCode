namespace Core.Context;

/// <summary>
/// 撤回最后一轮对话操作处理器
/// </summary>
[Register]
public sealed partial class RewindLastTurnHandler : IChatAdminOperationHandler
{
    public ChatAdminOperation Operation => ChatAdminOperation.RewindLastTurn;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        context.RewindResult = await context.ContextManager.RewindLastTurnAsync(ct).ConfigureAwait(false);
    }
}
