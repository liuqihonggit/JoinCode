namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ThinkingModeStampResult
{
    public IReadOnlyList<ApiMessage> Messages { get; init; } = [];
    public int StampedCount { get; init; }
}

public static class ThinkingModeStamp
{
    public static ThinkingModeStampResult Stamp(IReadOnlyList<ApiMessage> messages, bool isThinkingMode)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (!isThinkingMode || messages.Count == 0)
        {
            return new ThinkingModeStampResult { Messages = messages, StampedCount = 0 };
        }

        var result = new List<ApiMessage>(messages.Count);
        var stampedCount = 0;

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];

            if (msg.Role != MessageRole.Assistant)
            {
                result.Add(msg);
                continue;
            }

            if (msg.Metadata != null && msg.Metadata.ContainsKey("reasoning_content"))
            {
                result.Add(msg);
                continue;
            }

            stampedCount++;
            var newMetadata = new Dictionary<string, JsonElement>();
            if (msg.Metadata != null)
            {
                foreach (var kvp in msg.Metadata)
                {
                    newMetadata[kvp.Key] = kvp.Value;
                }
            }
            newMetadata["reasoning_content"] = JsonElementHelper.FromString("");

            result.Add(new ApiMessage(msg.Role, msg.Content, newMetadata, msg.ModelId, msg.TokenUsage));
        }

        return new ThinkingModeStampResult { Messages = result, StampedCount = stampedCount };
    }

    public static ThinkingModeStampResult Stamp(IReadOnlyList<ApiMessage> messages, string modelId)
    {
        return Stamp(messages, IsThinkingModeModel(modelId));
    }

    public static bool IsThinkingModeModel(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;

        var model = Configuration.Llm.ModelConfigLoader.FindModelByModelId(modelId);
        return model?.Capabilities.ThinkingMode ?? false;
    }
}
