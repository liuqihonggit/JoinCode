
namespace Core.Context.Compact;

/// <summary>
/// 消息分组服务实现 — 按 API 轮次将消息分组，以助手消息 ID 为边界切分
/// </summary>
[Register(typeof(IMessageGroupingService), ServiceLifetime.Singleton)]
public sealed partial class MessageGroupingService : ServiceEntity, IMessageGroupingService {
    /// <summary>
    /// 按 API 轮次分组消息 — 以助手消息 ID 变化为边界切分
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <returns>分组后的消息列表，每组对应一个 API 轮次</returns>
    public IReadOnlyList<IReadOnlyList<ApiMessage>> GroupMessagesByApiRound(IReadOnlyList<ApiMessage> messages) {
        ArgumentNullException.ThrowIfNull(messages);

        var groups = new List<IReadOnlyList<ApiMessage>>();
        var current = new List<ApiMessage>();
        string? lastAssistantId = null;

        foreach (var msg in messages) {
            var msgId = GetAssistantMessageId(msg);

            if (msg.Role == MessageRole.Assistant && msgId != lastAssistantId && current.Count > 0) {
                groups.Add(current);
                current = [msg];
            } else {
                current.Add(msg);
            }

            if (msg.Role == MessageRole.Assistant && msgId is not null) {
                lastAssistantId = msgId;
            }
        }

        if (current.Count > 0) {
            groups.Add(current);
        }

        return groups;
    }

    private static string? GetAssistantMessageId(ApiMessage msg) {
        if (msg.Role != MessageRole.Assistant || msg.Metadata is null) {
            return null;
        }

        if (msg.Metadata.TryGetValue("message_id", out var idObj) && idObj.ValueKind == JsonValueKind.String) {
            return idObj.GetString();
        }

        return null;
    }
}