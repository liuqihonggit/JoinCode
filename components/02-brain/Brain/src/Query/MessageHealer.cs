namespace JoinCode.Abstractions.LLM.Chat;

public static class MessageHealer
{
    private const int DefaultMaxToolResultChars = 50000;
    private const string SyntheticToolResultPlaceholder = "<tool_use_error>Tool call was interrupted or its result was lost. Please retry if needed.</tool_use_error>";

    public static IReadOnlyList<ApiMessage> Heal(
        IReadOnlyList<ApiMessage> messages,
        int maxToolResultChars = DefaultMaxToolResultChars)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0) return [];

        var result = new List<ApiMessage>(messages.Count);
        var pendingToolCallIds = new HashSet<string>();

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];

            if (msg.Role == MessageRole.Assistant && HasToolCalls(msg))
            {
                var toolCallIds = ExtractToolCallIds(msg);
                foreach (var id in toolCallIds)
                {
                    pendingToolCallIds.Add(id);
                }

                var isLast = i == messages.Count - 1;
                var nextIsToolResult = !isLast && messages[i + 1].Role == MessageRole.Tool;

                if (isLast || !nextIsToolResult)
                {
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                    {
                        result.Add(new ApiMessage(MessageRole.Assistant, msg.Content));
                    }
                    else
                    {
                        result.Add(new ApiMessage(MessageRole.Assistant, "[Tool use interrupted]"));
                    }

                    foreach (var id in toolCallIds)
                    {
                        pendingToolCallIds.Remove(id);
                        result.Add(CreateSyntheticToolResult(id));
                    }
                    continue;
                }

                result.Add(msg);
                continue;
            }

            if (msg.Role == MessageRole.Tool)
            {
                var toolCallId = ExtractToolCallId(msg);
                if (toolCallId == null || !pendingToolCallIds.Contains(toolCallId))
                {
                    continue;
                }

                pendingToolCallIds.Remove(toolCallId);

                var content = msg.Content;
                if (content != null && content.Length > maxToolResultChars)
                {
                    content = string.Concat(
                        content.AsSpan(0, maxToolResultChars),
                        "\n...[truncated]");
                }

                result.Add(new ApiMessage(MessageRole.Tool, content, msg.Metadata));
                continue;
            }

            result.Add(msg);
        }

        return result;
    }

    private static ApiMessage CreateSyntheticToolResult(string toolCallId)
    {
        return new ApiMessage(
            MessageRole.Tool,
            SyntheticToolResultPlaceholder,
            new Dictionary<string, JsonElement>
            {
                ["ToolCallId"] = JsonElementHelper.FromString(toolCallId),
                ["IsSynthetic"] = JsonElementHelper.FromBoolean(true)
            });
    }

    private static bool HasToolCalls(ApiMessage msg)
    {
        return msg.Metadata != null &&
            (msg.Metadata.ContainsKey("ToolCall") || msg.Metadata.ContainsKey("ToolCalls"));
    }

    private static List<string> ExtractToolCallIds(ApiMessage msg)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>();

        if (msg.Metadata != null && msg.Metadata.TryGetValue("ToolCallId", out var idObj) && idObj.TryGetString(out var id))
        {
            if (seen.Add(id!))
                ids.Add(id!);
        }

        if (msg.Metadata != null && msg.Metadata.TryGetValue("ToolCalls", out var toolCallsObj))
        {
            if (toolCallsObj.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in toolCallsObj.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("Id", out var tcIdProp) &&
                        tcIdProp.ValueKind == JsonValueKind.String)
                    {
                        var tcIdStr = tcIdProp.GetString();
                        if (tcIdStr != null && seen.Add(tcIdStr))
                        {
                            ids.Add(tcIdStr);
                        }
                    }
                }
            }
        }

        return ids;
    }

    private static string? ExtractToolCallId(ApiMessage msg)
    {
        if (msg.Metadata != null && msg.Metadata.TryGetValue("ToolCallId", out var idObj) && idObj.TryGetString(out var id))
        {
            return id;
        }
        return null;
    }
}
