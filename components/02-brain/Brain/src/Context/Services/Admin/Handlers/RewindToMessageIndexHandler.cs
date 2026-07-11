namespace Core.Context;

/// <summary>
/// 撤回到指定消息索引操作处理器
/// </summary>
[Register]
public sealed partial class RewindToMessageIndexHandler : IChatAdminOperationHandler
{
    public ChatAdminOperation Operation => ChatAdminOperation.RewindToMessageIndex;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        context.RewindResult = await context.ContextManager.RewindToMessageIndexAsync(context.MessageIndex!.Value, ct).ConfigureAwait(false);
    }
}
