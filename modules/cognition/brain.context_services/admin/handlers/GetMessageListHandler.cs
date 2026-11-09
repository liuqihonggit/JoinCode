namespace Core.Context;

/// <summary>
/// 获取消息列表操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class GetMessageListHandler : ServiceEntity, IChatAdminOperationHandler {
    /// <summary>
    /// 获取该处理器负责的管理操作类型
    /// </summary>
    public ChatAdminOperation Operation => ChatAdminOperation.GetMessageList;

    /// <summary>
    /// 执行获取消息列表操作，将上下文管理器中的消息转换为 API 消息记录写入上下文
    /// </summary>
    /// <param name="context">管理操作上下文，结果写入 MessageList</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct) {
        var chatHistory = await context.ContextManager.GetMessageListAsync(ct).ConfigureAwait(false);
        context.MessageList = chatHistory
            .Select(m => new ApiMessageRecord {
                Role = m.Role.ToString(),
                Content = m.Content ?? string.Empty
            })
            .ToList();
    }
}