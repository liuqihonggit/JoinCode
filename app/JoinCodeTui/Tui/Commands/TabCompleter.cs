namespace JoinCode.Tui.Commands;

/// <summary>
/// Tab 补全器 — 根据当前输入补全斜杠命令。
/// 纯函数，无副作用。
/// </summary>
public static class TabCompleter
{
    private static readonly string[] SortedCommands =
    [
        "/apply", "/build", "/clear", "/clear-history", "/config",
        "/diff", "/exit", "/files", "/grep", "/help", "/history",
        "/load", "/model", "/open", "/patch", "/save", "/sessions",
        "/shell", "/test", "/tokens", "/undo",
    ];

    /// <summary>
    /// 补全当前输入。返回补全后的文本，或 null 表示无法补全。
    /// </summary>
    public static string? Complete(string input)
    {
        if (string.IsNullOrEmpty(input) || input[0] != '/')
            return null;

        var prefix = input.ToLowerInvariant();
        var exact = Array.Find(SortedCommands, c => c.Equals(prefix, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var match = Array.Find(SortedCommands, c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match;
    }
}
