

namespace McpToolDispatch;

/// <summary>
/// 推送通知工具处理器 — 向用户发送推送通知（支持 info/warning/error 级别）
/// </summary>
[McpToolDispatch(ToolCategory.Notification, Optional = true)]
public partial class PushNotificationToolHandlers
{
    private readonly INotificationService? _notificationService;
    private readonly ILogger<PushNotificationToolHandlers>? _logger;

    /// <summary>
    /// 初始化推送通知工具处理器
    /// </summary>
    /// <param name="notificationService">通知服务（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public PushNotificationToolHandlers(
        INotificationService? notificationService = null,
        ILogger<PushNotificationToolHandlers>? logger = null)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// 向用户发送推送通知
    /// </summary>
    /// <param name="title">通知标题</param>
    /// <param name="message">通知消息内容</param>
    /// <param name="level">通知级别：info/warning/error（默认 info）</param>
    /// <param name="persistent">是否持久显示（可选，默认 false）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.PushNotification, "Send push notification to user", "notification")]
    public async Task<ToolResult> PushNotificationAsync(
        [McpToolParameter("Notification title")] string title,
        [McpToolParameter("Notification message")] string message,
        [McpToolParameter("Notification level: info/warning/error (default info)", Required = false)] string level = "info",
        [McpToolParameter("Persistent display (optional, default false)", Required = false)] bool? persistent = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            return ToolResultBuilder.Error().WithText(L.T(StringKey.PushNotificationTitleCannotBeEmpty)).Build();
        if (string.IsNullOrWhiteSpace(message))
            return ToolResultBuilder.Error().WithText(L.T(StringKey.PushNotificationMessageCannotBeEmpty)).Build();

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

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Message}", L.T(StringKey.PushNotificationFailedLog));
            return ToolResultBuilder.Error().WithText(L.T(StringKey.PushNotificationFailed, ex.Message)).Build();
        }
    }
}
