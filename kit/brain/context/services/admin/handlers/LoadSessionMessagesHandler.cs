namespace Core.Context;

/// <summary>
/// 加载历史消息操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class LoadSessionMessagesHandler : ServiceEntity, IChatAdminOperationHandler
{

    /// <summary>
    /// 初始化 <see cref="LoadSessionMessagesHandler"/> 实例
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public LoadSessionMessagesHandler(ILogger<LoadSessionMessagesHandler>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<LoadSessionMessagesHandler>? _logger;

    /// <summary>
    /// 获取该处理器负责的管理操作类型
    /// </summary>
    public ChatAdminOperation Operation => ChatAdminOperation.LoadSessionMessages;

    /// <summary>
    /// 执行加载历史消息操作，先清空当前消息再按角色依次重放上下文中的消息
    /// </summary>
    /// <param name="context">管理操作上下文，需提供 Messages</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        try
        {
            await context.ContextManager.ClearMessagesAsync(ct).ConfigureAwait(false);

            foreach (var msg in context.Messages)
            {
                if (msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                {
                    await context.ContextManager.AddUserMessageAsync(msg.Content, cancellationToken: ct).ConfigureAwait(false);
                }
                else if (msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                {
                    await context.ContextManager.AddAssistantMessageAsync(msg.Content, ct).ConfigureAwait(false);
                }
                else if (msg.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                {
                    await context.ContextManager.AddSystemMessageAsync(msg.Content, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            context.Error = ex;
        }
    }
}
