namespace JoinCode.Gui.WebHost;

/// <summary>
/// 子代理实例目录回放器（TASK058 B8，翻译单点）：journal 的 tool-workflow/agent-start 事件
/// → <see cref="DshSubagentCatalogEntry"/>（实例目录含已完成条目，重启后可还原；
/// 前端运行态由会话状态另行查询）。createdAt 取事件时间戳。
/// </summary>
internal static class DshSubagentCatalogProjector {
    /// <summary>从 journal 事件回放目录（无事件返回空列表 — 前端 ready 态）</summary>
    internal static List<DshSubagentCatalogEntry> Project(IReadOnlyList<DshSessionEvent> events) {
        var entries = new List<DshSubagentCatalogEntry>();
        foreach (var sessionEvent in events) {
            if (!string.Equals(sessionEvent.Type, "tool-workflow/agent-start", StringComparison.Ordinal)) {
                continue;
            }
            if (ReadString(sessionEvent.Data, "childId") is not { } childId) {
                continue;
            }
            entries.Add(new DshSubagentCatalogEntry {
                Id = childId,
                CreatedAt = sessionEvent.Time,
                Mode = "one-shot",
                Label = ReadString(sessionEvent.Data, "label"),
            });
        }
        return entries;
    }

    /// <summary>取 data 树的字符串字段（缺失/非字符串返回 null）</summary>
    private static string? ReadString(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
