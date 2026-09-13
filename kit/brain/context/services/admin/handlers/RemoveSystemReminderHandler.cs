namespace Core.Context;

/// <summary>
/// 移除系统提醒操作处理器
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class RemoveSystemReminderHandler : ServiceEntity, IChatAdminOperationHandler
{
    private readonly IChatPromptManager _promptManager;

    /// <summary>
    /// 初始化 <see cref="RemoveSystemReminderHandler"/> 实例
    /// </summary>
    /// <param name="promptManager">聊天提示词管理器，用于移除系统提醒</param>
    public RemoveSystemReminderHandler(IChatPromptManager promptManager)
    {
        _promptManager = promptManager;
    }

    /// <summary>
    /// 获取该处理器负责的管理操作类型
    /// </summary>
    public ChatAdminOperation Operation => ChatAdminOperation.RemoveSystemReminder;

    /// <summary>
    /// 执行移除系统提醒操作，按提醒标识从提示词管理器中移除对应提醒
    /// </summary>
    /// <param name="context">管理操作上下文，需提供 ReminderId</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        await _promptManager.RemoveReminderAsync(context.ReminderId ?? throw new InvalidOperationException("ReminderId is required."), ct).ConfigureAwait(false);
    }
}
