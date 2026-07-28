

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Notification, Optional = true)]
public partial class PushNotificationToolHandlers
{
    private readonly INotificationService? _notificationService;
    [Inject] private readonly ILogger<PushNotificationToolHandlers>? _logger;

    public PushNotificationToolHandlers(
        INotificationService? notificationService = null,
        ILogger<PushNotificationToolHandlers>? logger = null)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.PushNotification, "Send push notification to user", "notification")]
    public async Task<ToolResult> PushNotificationAsync(
        [McpToolParameter("Notification title")] string title,
        [McpToolParameter("Notification message")] string message,
        [McpToolParameter("Notification level: info/warning/error (default info)", Required = false)] string level = "info",
        [McpToolParameter("Persistent display (optional, default false)", Required = false)] bool? persistent = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            return McpResultBuilder.Error().WithText(L.T(StringKey.PushNotificationTitleCannotBeEmpty)).Build();
        if (string.IsNullOrWhiteSpace(message))
            return McpResultBuilder.Error().WithText(L.T(StringKey.PushNotificationMessageCannotBeEmpty)).Build();

        var notificationLevel = NotificationTypeExtensions.FromValue(level) ?? NotificationType.Info;

        try
        {
            if (_notificationService != null && _notificationService.IsAvailable)
            {
                await _notificationService.NotifyAsync(title, message, cancellationToken).ConfigureAwait(false);
                _logger?.LogDebug("{Message}", L.T(StringKey.PushNotificationSentViaServiceLog, title));
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.PushNotificationSent));
            response.AppendLine(L.T(StringKey.PushNotificationLabelTitle, title));
            response.AppendLine(L.T(StringKey.PushNotificationLabelMessage, message));
            response.AppendLine(L.T(StringKey.PushNotificationLabelLevel, notificationLevel.ToValue()));

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.PushNotificationFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.PushNotificationFailed, ex.Message)).Build();
        }
    }
}
