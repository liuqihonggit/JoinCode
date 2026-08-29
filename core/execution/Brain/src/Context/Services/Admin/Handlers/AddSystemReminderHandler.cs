namespace Core.Context;

/// <summary>
/// 添加系统提醒操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class AddSystemReminderHandler : ServiceEntity, IChatAdminOperationHandler
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
            context.ReminderId ?? throw new InvalidOperationException("ReminderId is required."),
            context.ReminderContent ?? throw new InvalidOperationException("ReminderContent is required."),
            context.ReminderPriority ?? 0, ct).ConfigureAwait(false);
    }
}
