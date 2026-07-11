using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 移除系统提醒操作处理器
/// </summary>
[Register]
public sealed partial class RemoveSystemReminderHandler : IChatAdminOperationHandler
{
    private readonly IChatPromptManager _promptManager;

    public RemoveSystemReminderHandler(IChatPromptManager promptManager)
    {
        _promptManager = promptManager;
    }

    public ChatAdminOperation Operation => ChatAdminOperation.RemoveSystemReminder;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        await _promptManager.RemoveReminderAsync(context.ReminderId!, ct).ConfigureAwait(false);
    }
}
