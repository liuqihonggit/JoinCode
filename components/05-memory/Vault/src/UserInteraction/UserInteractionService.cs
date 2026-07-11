
namespace Services.UserInteraction;

/// <summary>
/// 用户交互服务默认实现（Headless/SDK 模式）
/// TUI 模式下由 Terminal 渲染层直接处理用户交互，不经过此服务
/// </summary>
[Register]
public sealed partial class UserInteractionService : IUserInteractionService
{
    [Inject] private readonly ILogger<UserInteractionService>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 询问用户问题
    /// </summary>
    public Task<UserInteractionResult> AskQuestionAsync(string question, List<string>? options = null, bool multiSelect = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        _logger?.LogInformation(L.T(StringKey.VaultLogHeadlessAsk), question);

        if (options?.Count > 0)
        {
            for (int i = 0; i < options.Count; i++)
            {
                _logger?.LogDebug(L.T(StringKey.VaultLogOption), i + 1, options[i]);
            }
        }

        return Task.FromResult(new UserInteractionResult(true, "headless-auto-approved"));
    }

    public Task SendMessageAsync(string message, MessageType messageType = MessageType.Info, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        RecordInteractionMetrics("send_message", messageType.ToString());
        switch (messageType)
        {
            case MessageType.Warning:
                _logger?.LogWarning("{Message}", message);
                break;
            case MessageType.Error:
                _logger?.LogError("{Message}", message);
                break;
            case MessageType.Success:
                _logger?.LogInformation(L.T(StringKey.VaultLogSuccess), message);
                break;
            default:
                _logger?.LogInformation("{Message}", message);
                break;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 请求用户确认
    /// </summary>
    public Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _logger?.LogInformation(L.T(StringKey.VaultLogHeadlessConfirm), message);
        RecordInteractionMetrics("confirm", "auto_approved");
        return Task.FromResult(true);
    }

    private void RecordInteractionMetrics(string operation, string detail)
        => _telemetryService?.RecordCount("user.interaction.count", new Dictionary<string, string> { ["operation"] = operation, ["detail"] = detail }, "count", "User interaction count");
}
