using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 提醒注入中间件 — 检查工具空闲提醒并注入系统提醒
/// </summary>
[Register(typeof(IPreparePreprocessMiddleware))]
public sealed partial class ReminderInjectionMiddleware : IPreparePreprocessMiddleware
{
    [Inject] private readonly ToolIdleReminderService _toolIdleReminder;
    [Inject] private readonly ISystemReminderManager _reminderManager;
    [Inject] private readonly IChatContextManager _contextManager;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct)
    {
        var idleReminders = await _toolIdleReminder.CheckAndGenerateRemindersAsync(ct).ConfigureAwait(false);
        if (idleReminders.Count > 0)
        {
            await Task.WhenAll(idleReminders.Select(ir =>
                _reminderManager.AddReminderAsync($"tool-idle-{ir.ToolName}", ir.Message, priority: 80, ct: ct))).ConfigureAwait(false);
        }

        var reminders = await _reminderManager.FormatAsSystemRemindersAsync().ConfigureAwait(false);
        context.FormattedReminders = reminders;

        if (!string.IsNullOrWhiteSpace(reminders))
        {
            await _contextManager.AddDynamicSystemMessageAsync(reminders, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
