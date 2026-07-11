using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 设置系统提示词操作处理器
/// </summary>
[Register]
public sealed partial class SetSystemPromptHandler : IChatAdminOperationHandler
{
    [Inject] private readonly ILogger<SetSystemPromptHandler>? _logger;

    public ChatAdminOperation Operation => ChatAdminOperation.SetSystemPrompt;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        await context.ContextManager.UpdateSystemPromptAsync(context.SystemPrompt!, ct).ConfigureAwait(false);
    }
}
