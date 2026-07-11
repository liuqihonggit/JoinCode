namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Brief)]
public class BriefToolHandlers
{
    private readonly IBriefModeService _briefModeService;
    private readonly IBriefService? _briefService;
    private readonly IEntitlementService? _entitlementService;

    public BriefToolHandlers(IBriefModeService briefModeService, IBriefService? briefService = null, IEntitlementService? entitlementService = null)
    {
        _briefModeService = briefModeService ?? throw new ArgumentNullException(nameof(briefModeService));
        _briefService = briefService;
        _entitlementService = entitlementService;
    }

    [McpTool(SystemToolNameConstants.BriefMode, "Enable or disable brief mode (compact output)", "mode")]
    public Task<ToolResult> BriefModeAsync(
        [McpToolParameter("true to enable, false to disable")] bool enabled,
        CancellationToken cancellationToken = default)
    {
        // 对齐 TS: Entitlement check only gates the on-transition — off is always allowed
        if (enabled && _entitlementService is not null && !_entitlementService.IsBriefEntitled)
        {
            return Task.FromResult(McpResultBuilder.Error().WithText("Brief tool is not enabled for your account").Build());
        }

        var wasEnabled = _briefModeService.IsEnabled;

        if (enabled)
            _briefModeService.Enable();
        else
            _briefModeService.Disable();

        var isNowEnabled = _briefModeService.IsEnabled;
        var sb = new StringBuilder(128);

        if (wasEnabled != isNowEnabled)
            sb.AppendLine(isNowEnabled ? "Brief mode enabled" : "Brief mode disabled");
        else
            sb.AppendLine(isNowEnabled ? "Brief mode already enabled" : "Brief mode already disabled");

        return Task.FromResult(McpResultBuilder.Success().WithText(sb.ToString()).Build());
    }

    [McpTool(SystemToolNameConstants.BriefStatus, "Get current brief mode status", "mode")]
    public Task<ToolResult> BriefStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var status = _briefModeService.GetStatus();
        var sb = new StringBuilder(128);
        sb.AppendLine($"Brief mode: {(status.IsEnabled ? "enabled" : "disabled")}");
        sb.AppendLine(status.Description);
        if (status.EnabledAt.HasValue)
            sb.AppendLine($"Enabled at: {status.EnabledAt.Value:yyyy-MM-dd HH:mm:ss}");

        return Task.FromResult(McpResultBuilder.Success().WithText(sb.ToString()).Build());
    }

    [McpTool(SystemToolNameConstants.SendUserMessage, "Send a message to the user, supports markdown and file attachments", "messaging")]
    public Task<ToolResult> SendUserMessageAsync(
        [McpToolParameter("The message for the user. Supports markdown formatting.")] string message,
        [McpToolParameter("Optional file paths to attach (photos, screenshots, diffs, logs)", Required = false)] string[]? attachments = null,
        [McpToolParameter("Use 'proactive' for unsolicited updates, 'normal' for replies", Required = false)] string? status = null,
        CancellationToken cancellationToken = default)
    {
        // 对齐 TS: isBriefEnabled() — 需要 entitlement + userMsgOptIn
        if (_entitlementService is not null && !_entitlementService.IsBriefEnabled)
        {
            return Task.FromResult(McpResultBuilder.Error().WithText("Brief tool is not currently enabled").Build());
        }

        if (_briefService == null)
            return Task.FromResult(McpResultBuilder.Error().WithText("Brief service not initialized").Build());

        if (string.IsNullOrWhiteSpace(message))
            return Task.FromResult(McpResultBuilder.Error().WithText("Message cannot be empty").Build());

        if (attachments is not null)
        {
            foreach (var path in attachments)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return Task.FromResult(McpResultBuilder.Error().WithText("Attachment path cannot be empty").Build());
            }
        }

        try
        {
            var messageStatus = MessageStatusExtensions.FromValue(status);
            var isProactive = messageStatus == MessageStatus.Proactive;
            var result = _briefService.FormatMessageWithPaths(message, attachments, isProactive);

            var attachmentCount = attachments?.Length ?? 0;
            var suffix = attachmentCount > 0 ? $" ({attachmentCount} attachment{(attachmentCount > 1 ? "s" : "")} included)" : "";

            return Task.FromResult(McpResultBuilder.Success()
                .WithText($"Message delivered to user.{suffix}")
                .Build());
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpResultBuilder.Error().WithText($"Failed to send message: {ex.Message}").Build());
        }
    }
}