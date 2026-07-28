namespace McpClient;

/// <summary>
/// 结构化消息解析器 — 对齐 TS SendMessageTool backfillObservableInput + isStructuredProtocolMessage
/// </summary>
public static class StructuredMessageParser
{
    /// <summary>
    /// 尝试将 message 字符串解析为结构化协议消息
    /// </summary>
    public static bool TryParse(string? message, out StructuredMessageData? data)
    {
        data = null;

        if (string.IsNullOrWhiteSpace(message))
            return false;

        var span = message.AsSpan();
        var trimmed = span.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return false;

        try
        {
            var json = JsonSerializer.Deserialize(message, ContractsJsonContext.Default.DictionaryStringJsonElement);
            if (json is null || !json.TryGetValue("type", out var typeElement))
                return false;

            var typeValue = typeElement.GetString();
            if (string.IsNullOrEmpty(typeValue))
                return false;

            var structuredType = TeammateMessageTypeExtensions.FromValue(typeValue);
            if (structuredType is null)
                return false;

            data = new StructuredMessageData
            {
                Type = structuredType.Value,
                RequestId = json.TryGetValue("request_id", out var reqId) ? reqId.GetString() : null,
                Approve = json.TryGetValue("approve", out var approve) ? approve.ValueKind == JsonValueKind.True : (bool?)null,
                Reason = json.TryGetValue("reason", out var reason) ? reason.GetString() : null,
                Feedback = json.TryGetValue("feedback", out var feedback) ? feedback.GetString() : null,
                Content = json.TryGetValue("content", out var content) ? content.GetString() : null,
                Payload = json
            };

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}


