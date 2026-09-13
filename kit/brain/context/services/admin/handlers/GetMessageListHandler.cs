namespace Core.Context;

/// <summary>
/// 获取消息列表操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class GetMessageListHandler : ServiceEntity, IChatAdminOperationHandler
{
    public ChatAdminOperation Operation => ChatAdminOperation.GetMessageList;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        var chatHistory = await context.ContextManager.GetMessageListAsync(ct).ConfigureAwait(false);
        context.MessageList = chatHistory
            .Select(m => new ApiMessageRecord
            {
                Role = m.Role.ToString(),
                Content = m.Content ?? string.Empty
            })
            .ToList();
    }
}
