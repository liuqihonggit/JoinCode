namespace Core.Context;

public static class ToolReferenceExtractor
{
    public static HashSet<string> ExtractDiscoveredToolNames(MessageList history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var discovered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var msg in history)
        {
            if (msg.Role != MessageRole.Tool)
                continue;

            if (msg.Metadata != null &&
                msg.Metadata.TryGetValue("ToolReferences", out var refsEl) &&
                refsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in refsEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var name = item.GetString();
                        if (name is not null)
                            discovered.Add(name);
                    }
                }
            }
        }

        return discovered;
    }

    public static HashSet<string> ExtractDiscoveredToolNames(IReadOnlyList<ApiMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var discovered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var msg in messages)
        {
            if (msg.Role != MessageRole.Tool)
                continue;

            if (msg.Metadata != null &&
                msg.Metadata.TryGetValue("ToolReferences", out var refsEl) &&
                refsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in refsEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var name = item.GetString();
                        if (name is not null)
                            discovered.Add(name);
                    }
                }
            }
        }

        return discovered;
    }
}
