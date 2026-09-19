namespace Core.Context;

/// <summary>
/// 提醒注入中间件 — 检查工具空闲提醒并注入系统提醒
/// </summary>
[Register(typeof(IPreparePreprocessMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ReminderInjectionMiddleware : ServiceEntity, IPreparePreprocessMiddleware {

    /// <summary>
    /// 初始化提醒注入中间件
    /// </summary>
    /// <param name="toolIdleReminder">工具空闲提醒服务</param>
    /// <param name="reminderManager">系统提醒管理器</param>
    /// <param name="contextManager">聊天上下文管理器</param>
    public ReminderInjectionMiddleware(ToolIdleReminderService toolIdleReminder, ISystemReminderManager reminderManager, IChatContextManager contextManager) {
        _toolIdleReminder = toolIdleReminder;
        _reminderManager = reminderManager;
        _contextManager = contextManager;
    }
    private readonly ToolIdleReminderService _toolIdleReminder;
    private readonly ISystemReminderManager _reminderManager;
    private readonly IChatContextManager _contextManager;

    /// <summary>错误行为策略：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct) {
        var idleReminders = await _toolIdleReminder.CheckAndGenerateRemindersAsync(ct).ConfigureAwait(false);
        if (idleReminders.Count > 0) {
            await Task.WhenAll(idleReminders.Select(ir =>
                _reminderManager.AddReminderAsync($"tool-idle-{ir.ToolName}", ir.Message, priority: 80, ct: ct))).ConfigureAwait(false);
        }

        var reminders = await _reminderManager.FormatAsSystemRemindersAsync().ConfigureAwait(false);
        context.FormattedReminders = reminders;

        if (!string.IsNullOrWhiteSpace(reminders)) {
            await _contextManager.AddDynamicSystemMessageAsync(reminders, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}