namespace Core.Context;

/// <summary>
/// 后台通知处理器接口 — 检查并注入后台代理通知
/// </summary>
public interface IBackgroundNotificationHandler
{
    /// <summary>
    /// 处理待处理的后台通知，将通知内容注入到对话历史中
    /// </summary>
    /// <returns>注入的通知数量</returns>
    Task<int> ProcessPendingNotificationsAsync(CancellationToken ct);
}

/// <summary>
/// 后台通知处理器 — 从通知队列获取后台代理完成的通知并注入对话历史
/// </summary>
[Register(typeof(IBackgroundNotificationHandler))]
public sealed partial class BackgroundNotificationHandler : IBackgroundNotificationHandler
{
    private readonly IAgentNotificationQueue? _notificationQueue;
    private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<BackgroundNotificationHandler>? _logger;

    public BackgroundNotificationHandler(
        IChatContextManager contextManager,
        IAgentNotificationQueue? notificationQueue = null,
        ILogger<BackgroundNotificationHandler>? logger = null)
    {
        _contextManager = contextManager;
        _notificationQueue = notificationQueue;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> ProcessPendingNotificationsAsync(CancellationToken ct)
    {
        if (_notificationQueue is null || !_notificationQueue.HasPendingNotifications)
            return 0;

        var pendingNotifications = _notificationQueue.DequeueAll();
        if (pendingNotifications.Count == 0)
            return 0;

        foreach (var notification in pendingNotifications)
        {
            var isShellNotification = notification.Xml.Contains("<task-notification>", StringComparison.OrdinalIgnoreCase);
            var wrappedContent = isShellNotification
                ? notification.Xml
                : $"A background agent completed a task:\n{notification.Xml}";
            await _contextManager.AddUserMessageAsync(wrappedContent, ct).ConfigureAwait(false);
        }

        _logger?.LogInformation("[BackgroundNotificationHandler] 注入 {Count} 条后台代理通知", pendingNotifications.Count);
        return pendingNotifications.Count;
    }
}
