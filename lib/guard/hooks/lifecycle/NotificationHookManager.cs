namespace Core.Hooks.Lifecycle;

/// <summary>
/// 通知钩子管理器接口 — 将通知事件分发给已注册的 Notification 钩子
/// </summary>
public interface INotificationHookManager
{
    /// <summary>
    /// 异步触发通知钩子链
    /// </summary>
    /// <param name="context">通知钩子上下文</param>
    /// <param name="ct">取消令牌</param>
    Task OnNotificationAsync(NotificationHookContext context, CancellationToken ct = default);
}

/// <summary>
/// 通知钩子上下文 — 描述一次通知事件的会话、类型、消息与附加数据
/// </summary>
public sealed partial class NotificationHookContext
{
    /// <summary>会话 ID</summary>
    public required string SessionId { get; init; }
    /// <summary>通知类型（用作钩子匹配器）</summary>
    public required string NotificationType { get; init; }
    /// <summary>通知消息文本</summary>
    public required string Message { get; init; }
    /// <summary>附加数据负载</summary>
    public Dictionary<string, JsonElement> Data { get; init; } = new();
}

/// <summary>
/// 通知钩子管理器 — 通过 IHookOrchestrator 执行 Notification 事件钩子并记录遥测
/// </summary>
[Register(typeof(INotificationHookManager), ServiceLifetime.Singleton)]
public sealed partial class NotificationHookManager : ServiceEntity, INotificationHookManager
{
    private readonly IHookOrchestrator _orchestrator;
    private readonly ILogger<NotificationHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造通知钩子管理器
    /// </summary>
    /// <param name="orchestrator">钩子编排器</param>
    /// <param name="logger">日志器，可为空</param>
    /// <param name="telemetryService">遥测服务，可为空</param>
    public NotificationHookManager(IHookOrchestrator orchestrator, ILogger<NotificationHookManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <inheritdoc />
    public async Task OnNotificationAsync(NotificationHookContext context, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, JsonElement>
        {
            ["sessionId"] = JsonElementHelper.FromString(context.SessionId),
            ["notificationType"] = JsonElementHelper.FromString(context.NotificationType),
            ["message"] = JsonElementHelper.FromString(context.Message),
            ["data"] = JsonSerializer.SerializeToElement(context.Data, HooksJsonContext.Default.DictionaryStringJsonElement)
        };

        await foreach (var result in _orchestrator.ExecuteHooksAsync(
            HookEvent.Notification,
            payload,
            matcher: context.NotificationType,
            sessionId: context.SessionId,
            cancellationToken: ct).ConfigureAwait(false))
        {
            if (result.Outcome == HookOutcome.NonBlockingError)
            {
                _logger?.LogWarning("Notification hook error for session {SessionId}, type {NotificationType}: {Message}",
                    context.SessionId, context.NotificationType, result.Message);
            }
        }

        _telemetryService?.RecordCount("hook.notification.count", new() { ["type"] = context.NotificationType, ["success"] = true.ToString() }, description: "Notification hook execution count");
    }
}
