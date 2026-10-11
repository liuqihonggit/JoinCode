
namespace Core.Agents;

/// <summary>
/// 转录记录转换器 — 将 TranscriptEntry 列表转换为 MessageList，支持追加新提示
/// </summary>
internal static class TranscriptConverter {
    /// <summary>
    /// 将转录记录列表转换为消息列表，跳过空内容与未知角色
    /// </summary>
    /// <param name="entries">转录记录列表</param>
    /// <returns>转换后的消息列表</returns>
    public static MessageList ToMessageList(IReadOnlyList<TranscriptEntry> entries) {
        var history = new MessageList();

        foreach (var entry in entries) {
            var role = MapRole(entry.Role);
            if (role is null) continue;

            if (string.IsNullOrWhiteSpace(entry.Content)) continue;

            history.Add(new ApiMessage(role.Value, entry.Content));
        }

        return history;
    }

    /// <summary>
    /// 将转录记录列表转换为消息列表并追加新的用户提示
    /// </summary>
    /// <param name="entries">转录记录列表</param>
    /// <param name="newPrompt">要追加的新用户提示</param>
    /// <returns>包含历史记录与新提示的消息列表</returns>
    public static MessageList ToMessageListWithNewPrompt(IReadOnlyList<TranscriptEntry> entries, string newPrompt) {
        var history = ToMessageList(entries);

        history.AddUserMessage(newPrompt);

        return history;
    }

    private static MessageRole? MapRole(string role) {
        var mapped = MessageRoleExtensions.FromValue(role);
        if (mapped is not null) return mapped;
        // "error" 角色映射为 Assistant
        return role.Equals("error", StringComparison.OrdinalIgnoreCase) ? MessageRole.Assistant : null;
    }
}