namespace Core.Agents;

/// <summary>
/// Worktree 路径模式匹配器 — 封装 ephemeral worktree 目录名的正则模式匹配逻辑。
/// <para>与 <see cref="WorktreeIncludePatternMatcher"/> 互补：后者匹配文件路径 glob，本类匹配目录名正则。</para>
/// <para>非法正则模式会被吞掉异常并视为不匹配，保证清理流程健壮性。</para>
/// </summary>
internal static class WorktreePatternMatcher
{
    /// <summary>
    /// 判断指定目录名是否匹配任一 ephemeral worktree 模式（大小写不敏感正则匹配）。
    /// </summary>
    /// <param name="dirName">待匹配的目录名</param>
    /// <param name="patterns">ephemeral 模式列表（正则表达式字符串）</param>
    /// <returns>匹配任一模式返回 true；模式列表为空或全部不匹配返回 false；非法正则视为不匹配</returns>
    public static bool IsEphemeralWorktree(string dirName, IReadOnlyList<string> patterns)
    {
        return patterns.Any(pattern => IsSinglePatternMatch(dirName, pattern));
    }

    private static bool IsSinglePatternMatch(string dirName, string pattern)
    {
        try
        {
            return Regex.IsMatch(dirName, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
