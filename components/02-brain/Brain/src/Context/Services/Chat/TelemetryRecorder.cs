namespace Core.Context;

/// <summary>
/// 遥测记录器接口 — 记录每轮对话的遥测快照
/// </summary>
public interface ITelemetryRecorder
{
    /// <summary>
    /// 记录每轮对话的遥测快照
    /// </summary>
    void RecordTurnTelemetry(MessageList historySnapshot, int turnIndex);
}

/// <summary>
/// 遥测记录器 — 记录每轮对话的消息列表快照到遥测系统
/// </summary>
[Register(typeof(ITelemetryRecorder))]
public sealed class TelemetryRecorder : ITelemetryRecorder
{
    private readonly ITelemetryService? _telemetryService;

    public TelemetryRecorder(QueryLoopServices? services = null)
    {
        _telemetryService = services?.TelemetryService;
    }

    /// <inheritdoc/>
    public void RecordTurnTelemetry(MessageList historySnapshot, int turnIndex)
    {
        var chatSpan = _telemetryService?.StartSpan($"chat.turn.{turnIndex}", TelemetrySpanKind.Server);
        if (chatSpan == null) return;

        chatSpan.SetTag("turn.message_count", historySnapshot.Count);
        for (var mi = 0; mi < historySnapshot.Count; mi++)
        {
            var msg = historySnapshot[mi];
            var maxPreview = msg.Role == MessageRole.System ? 500 : 120;
            var contentPreview = msg.Content != null && msg.Content.Length > maxPreview
                ? msg.Content[..maxPreview] + "..."
                : msg.Content ?? "";
            chatSpan.SetTag($"turn.msg[{mi}].role", msg.Role.ToString());
            chatSpan.SetTag($"turn.msg[{mi}].len", msg.Content?.Length ?? 0);
            chatSpan.SetTag($"turn.msg[{mi}].preview", contentPreview);
        }
        chatSpan.SetStatus(TelemetryStatusCode.Ok);
        chatSpan.Dispose();
    }
}
