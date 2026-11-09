namespace Core.Context;

/// <summary>
/// 工具引用提取器，从消息历史的元数据中收集已发现的工具名称
/// </summary>
public static class ToolReferenceExtractor {
    /// <summary>
    /// 从消息列表中提取所有被引用的工具的名称
    /// </summary>
    /// <param name="history">消息历史</param>
    /// <returns>已发现的工具名称集合</returns>
    public static HashSet<string> ExtractDiscoveredToolNames(MessageList history) {
        ArgumentNullException.ThrowIfNull(history);
        var discovered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var msg in history) {
            if (msg.Role != MessageRole.Tool)
                continue;

            if (msg.Metadata != null &&
                msg.Metadata.TryGetValue("ToolReferences", out var refsEl) &&
                refsEl.ValueKind == JsonValueKind.Array) {
                foreach (var item in refsEl.EnumerateArray()) {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } name) {
                        discovered.Add(name);
                    }
                }
            }
        }

        return discovered;
    }

    /// <summary>
    /// 从消息列表中提取所有被引用工具的名称
    /// </summary>
    /// <param name="messages">消息列表</param>
    /// <returns>已发现的工具名称集合</returns>
    public static HashSet<string> ExtractDiscoveredToolNames(IReadOnlyList<ApiMessage> messages) {
        ArgumentNullException.ThrowIfNull(messages);
        var discovered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var msg in messages) {
            if (msg.Role != MessageRole.Tool)
                continue;

            if (msg.Metadata != null &&
                msg.Metadata.TryGetValue("ToolReferences", out var refsEl) &&
                refsEl.ValueKind == JsonValueKind.Array) {
                foreach (var item in refsEl.EnumerateArray()) {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } name) {
                        discovered.Add(name);
                    }
                }
            }
        }

        return discovered;
    }
}