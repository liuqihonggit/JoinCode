
namespace Core.Context.Compact;

[Register]
public sealed class MessageGroupingService : IMessageGroupingService
{
    public IReadOnlyList<IReadOnlyList<ApiMessage>> GroupMessagesByApiRound(IReadOnlyList<ApiMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var groups = new List<IReadOnlyList<ApiMessage>>();
        var current = new List<ApiMessage>();
        string? lastAssistantId = null;

        foreach (var msg in messages)
        {
            var msgId = GetAssistantMessageId(msg);

            if (msg.Role == MessageRole.Assistant && msgId != lastAssistantId && current.Count > 0)
            {
                groups.Add(current);
                current = [msg];
            }
            else
            {
                current.Add(msg);
            }

            if (msg.Role == MessageRole.Assistant && msgId is not null)
            {
                lastAssistantId = msgId;
            }
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups;
    }

    private static string? GetAssistantMessageId(ApiMessage msg)
    {
        if (msg.Role != MessageRole.Assistant || msg.Metadata is null)
        {
            return null;
        }

        if (msg.Metadata.TryGetValue("message_id", out var idObj) && idObj.ValueKind == JsonValueKind.String)
        {
            return idObj.GetString();
        }

        return null;
    }
}
