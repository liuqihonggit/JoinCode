namespace Core.Hooks.Lifecycle;

public interface INotificationHookManager
{
    Task OnNotificationAsync(NotificationHookContext context, CancellationToken ct = default);
}

public sealed partial class NotificationHookContext
{
    public required string SessionId { get; init; }
    public required string NotificationType { get; init; }
    public required string Message { get; init; }
    public Dictionary<string, JsonElement> Data { get; init; } = new();
}

[Register]
public sealed partial class NotificationHookManager : INotificationHookManager
{
    private readonly IHookOrchestrator _orchestrator;
    [Inject] private readonly ILogger<NotificationHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public NotificationHookManager(IHookOrchestrator orchestrator, ILogger<NotificationHookManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger;
        _telemetryService = telemetryService;
    }

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
