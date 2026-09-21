namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ThinkingModeStampResult {
    /// <summary>获取处理后的消息列表。</summary>
    public IReadOnlyList<ApiMessage> Messages { get; init; } = [];
    /// <summary>获取已加盖思维模式标记的消息数。</summary>
    public int StampedCount { get; init; }
}

public sealed class ThinkingModeStamp {
    private readonly IModelConfigLoader _modelConfigLoader;

    /// <summary>构造思维模式标记器。</summary>
    public ThinkingModeStamp(IModelConfigLoader modelConfigLoader) {
        _modelConfigLoader = modelConfigLoader;
    }

    /// <summary>为消息列表加盖思维模式标记。</summary>
    public ThinkingModeStampResult Stamp(IReadOnlyList<ApiMessage> messages, bool isThinkingMode) {
        ArgumentNullException.ThrowIfNull(messages);

        if (!isThinkingMode || messages.Count == 0) {
            return new ThinkingModeStampResult { Messages = messages, StampedCount = 0 };
        }

        var result = new List<ApiMessage>(messages.Count);
        var stampedCount = 0;

        for (var i = 0; i < messages.Count; i++) {
            var msg = messages[i];

            if (msg.Role != MessageRole.Assistant) {
                result.Add(msg);
                continue;
            }

            if (msg.Metadata != null && msg.Metadata.ContainsKey("reasoning_content")) {
                result.Add(msg);
                continue;
            }

            stampedCount++;
            var newMetadata = new Dictionary<string, JsonElement>();
            if (msg.Metadata != null) {
                foreach (var kvp in msg.Metadata) {
                    newMetadata[kvp.Key] = kvp.Value;
                }
            }
            newMetadata["reasoning_content"] = JsonElementHelper.FromString("");

            result.Add(new ApiMessage(msg.Role, msg.Content, newMetadata, msg.ModelId, msg.TokenUsage));
        }

        return new ThinkingModeStampResult { Messages = result, StampedCount = stampedCount };
    }

    /// <summary>根据模型 ID 为消息列表加盖思维模式标记。</summary>
    public ThinkingModeStampResult Stamp(IReadOnlyList<ApiMessage> messages, string modelId) {
        return Stamp(messages, IsThinkingModeModel(modelId));
    }

    /// <summary>判断指定模型是否支持思维模式。</summary>
    public bool IsThinkingModeModel(string modelId) {
        if (string.IsNullOrWhiteSpace(modelId)) return false;

        var model = _modelConfigLoader.FindModelByModelId(modelId);
        return model?.Capabilities.ThinkingMode ?? false;
    }
}