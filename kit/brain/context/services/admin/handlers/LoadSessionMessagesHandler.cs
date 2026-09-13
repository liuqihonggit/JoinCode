namespace Core.Context;

/// <summary>
/// 加载历史消息操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class LoadSessionMessagesHandler : ServiceEntity, IChatAdminOperationHandler
{

    public LoadSessionMessagesHandler(ILogger<LoadSessionMessagesHandler>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<LoadSessionMessagesHandler>? _logger;

    public ChatAdminOperation Operation => ChatAdminOperation.LoadSessionMessages;

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
