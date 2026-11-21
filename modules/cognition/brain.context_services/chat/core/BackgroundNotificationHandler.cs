namespace Core.Context;

/// <summary>
/// 后台通知处理器接口 — 检查并注入后台代理通知
/// </summary>
public interface IBackgroundNotificationHandler {
    /// <summary>
    /// 处理待处理的后台通知，将通知内容注入到对话历史中
    /// </summary>
    /// <returns>注入的通知数量</returns>
    Task<int> ProcessPendingNotificationsAsync(CancellationToken ct);

    /// <summary>
    /// 排空并返回待处理通知文本（不写聊天上下文 — 供轮内注入方自行消费）
    /// </summary>
    /// <returns>待注入的通知文本列表</returns>
    IReadOnlyList<string> DrainPendingNotificationTexts();

    /// <summary>
    /// 是否存在待处理通知（供循环判定是否继续一轮以注入通知）
    /// </summary>
    bool HasPendingNotifications { get; }
}

/// <summary>
/// 后台通知处理器 — 从通知队列获取后台代理完成的通知并注入对话历史
/// </summary>
[Register(typeof(IBackgroundNotificationHandler), ServiceLifetime.Singleton)]
public sealed partial class BackgroundNotificationHandler : ServiceEntity, IBackgroundNotificationHandler {
    private readonly IAgentNotificationQueue? _notificationQueue;
    private readonly IChatContextManager _contextManager;
    private readonly ILogger<BackgroundNotificationHandler>? _logger;

    /// <summary>
    /// 初始化后台通知处理器
    /// </summary>
    /// <param name="contextManager">聊天上下文管理器</param>
    /// <param name="notificationQueue">代理通知队列（可选，null 时跳过通知注入）</param>
    /// <param name="logger">可选日志记录器</param>
    public BackgroundNotificationHandler(
        IChatContextManager contextManager,
        IAgentNotificationQueue? notificationQueue = null,
        ILogger<BackgroundNotificationHandler>? logger = null) {
        _contextManager = contextManager;
        _notificationQueue = notificationQueue;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<int> ProcessPendingNotificationsAsync(CancellationToken ct) {
        var pendingNotifications = DrainPendingNotificationTexts();
        if (pendingNotifications.Count == 0)
            return 0;

        foreach (var notification in pendingNotifications) {
            await _contextManager.AddUserMessageAsync(notification, MessageOriginKind.TaskNotification, ct).ConfigureAwait(false);
        }

        _logger?.LogInformation("[BackgroundNotificationHandler] 注入 {Count} 条后台代理通知", pendingNotifications.Count);
        return pendingNotifications.Count;
    }

    /// <inheritdoc/>
    public bool HasPendingNotifications => _notificationQueue?.HasPendingNotifications == true;

    /// <inheritdoc/>
    public IReadOnlyList<string> DrainPendingNotificationTexts() {
        if (_notificationQueue is null || !_notificationQueue.HasPendingNotifications)
            return [];

        var pendingNotifications = _notificationQueue.DequeueAll();
        var texts = new List<string>(pendingNotifications.Count);
        foreach (var notification in pendingNotifications) {
            var isShellNotification = notification.Xml.Contains("<task-notification>", StringComparison.OrdinalIgnoreCase);
            texts.Add(isShellNotification
                ? notification.Xml
                : $"A background agent completed a task:\n{notification.Xml}");
        }

        return texts;
    }
}