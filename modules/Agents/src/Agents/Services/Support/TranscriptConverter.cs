
namespace Core.Agents;

internal static class TranscriptConverter
{
    public static MessageList ToMessageList(IReadOnlyList<TranscriptEntry> entries)
    {
        var history = new MessageList();

        foreach (var entry in entries)
        {
            var role = MapRole(entry.Role);
            if (role is null) continue;

            if (string.IsNullOrWhiteSpace(entry.Content)) continue;

            history.Add(new ApiMessage(role.Value, entry.Content));
        }

        return history;
    }

    public static MessageList ToMessageListWithNewPrompt(IReadOnlyList<TranscriptEntry> entries, string newPrompt)
    {
        var history = ToMessageList(entries);

        history.AddUserMessage(newPrompt);

        return history;
    }

    private static MessageRole? MapRole(string role)
    {
        var mapped = MessageRoleExtensions.FromValue(role);
        if (mapped is not null) return mapped;
        // "error" 角色映射为 Assistant
        return role.Equals("error", StringComparison.OrdinalIgnoreCase) ? MessageRole.Assistant : null;
    }
}
