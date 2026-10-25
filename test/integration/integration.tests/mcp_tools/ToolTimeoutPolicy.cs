namespace Integration.Tests.McpTools;

/// <summary>
/// 按分类超时策略 — 不同分类不同超时
/// </summary>
public static class ToolTimeoutPolicy {
    private static readonly FrozenDictionary<string, TimeSpan> CategoryTimeouts = new Dictionary<string, TimeSpan> {
        ["file"] = TimeSpan.FromSeconds(5),
        ["read"] = TimeSpan.FromSeconds(5),
        ["search"] = TimeSpan.FromSeconds(10),
        ["github"] = TimeSpan.FromSeconds(30),
        ["code_index"] = TimeSpan.FromSeconds(60),
        ["desktop"] = TimeSpan.FromSeconds(10),
        ["git"] = TimeSpan.FromSeconds(15),
        ["shell"] = TimeSpan.FromSeconds(15),
        ["web"] = TimeSpan.FromSeconds(20),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>默认超时</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>按工具分类获取超时</summary>
    public static TimeSpan GetTimeout(ToolInfo tool) {
        if (tool.Category is not null &&
            CategoryTimeouts.TryGetValue(tool.Category, out var timeout))
            return timeout;
        return DefaultTimeout;
    }
}
