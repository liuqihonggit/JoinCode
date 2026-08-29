namespace Core.Context;

/// <summary>
/// 设置系统提示词操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class SetSystemPromptHandler : ServiceEntity, IChatAdminOperationHandler
{

    public SetSystemPromptHandler(ILogger<SetSystemPromptHandler>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<SetSystemPromptHandler>? _logger;

    public ChatAdminOperation Operation => ChatAdminOperation.SetSystemPrompt;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        await context.ContextManager.UpdateSystemPromptAsync(context.SystemPrompt ?? throw new InvalidOperationException("SystemPrompt is required."), ct).ConfigureAwait(false);
    }
}
