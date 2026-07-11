using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 添加系统提醒操作处理器
/// </summary>
[Register]
public sealed partial class AddSystemReminderHandler : IChatAdminOperationHandler
{
    private readonly IChatPromptManager _promptManager;

    public AddSystemReminderHandler(IChatPromptManager promptManager)
    {
        _promptManager = promptManager;
    }

    public ChatAdminOperation Operation => ChatAdminOperation.AddSystemReminder;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        await _promptManager.AddReminderAsync(
            context.ReminderId!, context.ReminderContent!, context.ReminderPriority ?? 0, ct).ConfigureAwait(false);
    }
}
