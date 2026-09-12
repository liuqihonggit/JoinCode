namespace JoinCode.Tui.Commands;

/// <summary>
/// Tab 补全器 — 根据当前输入补全斜杠命令。
/// 命令列表从底层 ISlashCommandCatalog 获取，不硬编码。
/// </summary>
public static class TabCompleter
{
    /// <summary>
    /// 补全当前输入。返回补全后的文本，或 null 表示无法补全。
    /// </summary>
    /// <param name="input">用户输入。</param>
    /// <param name="commands">可用命令列表（如 ["/help", "/exit", ...]），从 ISlashCommandCatalog 获取。</param>
    public static string? Complete(string input, IReadOnlyList<string> commands)
    {
        if (string.IsNullOrEmpty(input) || input[0] != '/')
            return null;

        var prefix = input.ToLowerInvariant();
        var exact = Array.Find(commands.ToArray(), c => c.Equals(prefix, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var match = Array.Find(commands.ToArray(), c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match;
    }
}
