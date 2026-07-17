namespace JoinCode.Abstractions.LLM.Chat;

public static class CacheFirstMessageBuilder
{
    public static IReadOnlyList<ApiMessage> BuildMessages(
        ImmutablePrefix prefix,
        AppendOnlyLog log,
        string? pendingUser,
        string? dynamicContext = null,
        bool dynamicContextChanged = true,
        int maxToolResultChars = 50000,
        string? modelId = null)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(log);

        var messages = new List<ApiMessage>();

        foreach (var msg in prefix.ToMessages())
        {
            messages.Add(msg);
        }

        if (!string.IsNullOrWhiteSpace(dynamicContext))
        {
            if (dynamicContextChanged)
            {
                messages.Add(new ApiMessage(MessageRole.System, dynamicContext, CacheBreakMarker.Create()));
            }
            else
            {
                messages.Add(new ApiMessage(MessageRole.System, dynamicContext));
            }
        }

        var healedLog = MessageHealer.Heal(log.ToMessages(), maxToolResultChars);
        foreach (var msg in healedLog)
        {
            messages.Add(msg);
        }

        if (pendingUser is not null)
        {
            messages.Add(new ApiMessage(MessageRole.User, pendingUser));
        }

        if (modelId != null)
        {
            var stampResult = ThinkingModeStamp.Stamp(messages, modelId);
            return stampResult.Messages;
        }

        return messages;
    }
}
