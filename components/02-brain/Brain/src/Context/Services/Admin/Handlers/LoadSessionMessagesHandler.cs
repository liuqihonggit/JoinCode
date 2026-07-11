using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 加载历史消息操作处理器
/// </summary>
[Register]
public sealed partial class LoadSessionMessagesHandler : IChatAdminOperationHandler
{
    [Inject] private readonly ILogger<LoadSessionMessagesHandler>? _logger;

    public ChatAdminOperation Operation => ChatAdminOperation.LoadSessionMessages;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        try
        {
            await context.ContextManager.ClearMessagesAsync(ct).ConfigureAwait(false);

            foreach (var msg in context.Messages!)
            {
                if (msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                {
                    await context.ContextManager.AddUserMessageAsync(msg.Content, ct).ConfigureAwait(false);
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
