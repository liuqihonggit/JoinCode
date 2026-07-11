namespace Core.Context;

/// <summary>
/// 聊天空闲检测器 — 负责检测工具空闲轮次并注入提醒
/// 提取自 ChatService.HandleIdleDetectionAsync + DetectToolUsageAsync
/// </summary>
[Register]
public sealed partial class ChatIdleDetector : IChatIdleDetector
{
    [Inject] private readonly IChatContextManager _contextManager;
    [Inject] private readonly ISystemReminderManager _reminderManager;
    [Inject] private readonly ToolIdleReminderService _toolIdleReminder;
    [Inject] private readonly IdleToolDetector _idleDetector;
    [Inject] private readonly ILogger<ChatIdleDetector>? _logger;

    /// <summary>
    /// 记录助手轮次使用的工具名
    /// </summary>
    public void RecordAssistantTurn(string? toolNameUsed)
    {
        _toolIdleReminder.RecordAssistantTurn(toolNameUsed);
    }

    /// <summary>
    /// 检测空闲并注入提醒（如有需要）
    /// </summary>
    public async Task HandleIdleDetectionAsync(CancellationToken ct)
    {
        var usedTool = await DetectToolUsageAsync(ct).ConfigureAwait(false);
        _idleDetector.OnLlmResponse(usedTool);

        if (_idleDetector.ShouldInjectReminder())
        {
            var idleMessage = _idleDetector.GetReminderMessage();
            await _reminderManager.AddReminderAsync(
                "idle-tool-reminder",
                idleMessage,
                priority: 80,
                ct: ct).ConfigureAwait(false);
            _logger?.LogInformation("[IdleDetection] 已注入空闲工具提醒，连续 {Rounds} 轮未使用工具", _idleDetector.ConsecutiveNoToolRounds);
            _idleDetector.Reset();
        }
    }

    /// <summary>
    /// 重置空闲检测状态
    /// </summary>
    public void Reset()
    {
        _toolIdleReminder.Reset();
    }

    private async Task<bool> DetectToolUsageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var history = await _contextManager.GetMessageListAsync(cancellationToken).ConfigureAwait(false);
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Role == MessageRole.Tool)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[IdleDetection] 检测工具使用失败，默认未使用工具");
        }

        return false;
    }
}
