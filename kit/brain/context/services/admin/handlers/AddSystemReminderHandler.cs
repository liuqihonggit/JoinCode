namespace Core.Context;

/// <summary>
/// 添加系统提醒操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class AddSystemReminderHandler : ServiceEntity, IChatAdminOperationHandler
{
    private readonly IChatPromptManager _promptManager;

    /// <summary>
    /// 初始化 <see cref="AddSystemReminderHandler"/> 实例
    /// </summary>
    /// <param name="promptManager">聊天提示词管理器，用于添加系统提醒</param>
    public AddSystemReminderHandler(IChatPromptManager promptManager)
    {
        _promptManager = promptManager;
    }

    /// <summary>
    /// 获取该处理器负责的管理操作类型
    /// </summary>
    public ChatAdminOperation Operation => ChatAdminOperation.AddSystemReminder;

    /// <summary>
    /// 执行添加系统提醒操作，将提醒内容按标识和优先级写入提示词管理器
    /// </summary>
    /// <param name="context">管理操作上下文，需提供 ReminderId、ReminderContent 和 ReminderPriority</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        await _promptManager.AddReminderAsync(
            context.ReminderId ?? throw new InvalidOperationException("ReminderId is required."),
            context.ReminderContent ?? throw new InvalidOperationException("ReminderContent is required."),
            context.ReminderPriority ?? 0, ct).ConfigureAwait(false);
    }
}
