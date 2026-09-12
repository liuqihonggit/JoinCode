namespace Core.Context;

/// <summary>
/// 撤回到指定消息索引操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class RewindToMessageIndexHandler : ServiceEntity, IChatAdminOperationHandler
{
    public ChatAdminOperation Operation => ChatAdminOperation.RewindToMessageIndex;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        context.RewindResult = await context.ContextManager.RewindToMessageIndexAsync(context.MessageIndex ?? throw new InvalidOperationException("MessageIndex is required."), ct).ConfigureAwait(false);
    }
}
