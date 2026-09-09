namespace Core.Security.DangerClassification;

/// <summary>
/// 路径大小写守卫结果
/// </summary>
public sealed record PathCaseGuardResult(bool Blocked, string? SuggestedPath, string? Reason)
{
    /// <summary>
    /// 放行结果
    /// </summary>
    public static readonly PathCaseGuardResult Pass = new(false, null, null);
}

/// <summary>
/// 路径大小写守卫 — 拦截删除命令中路径大小写与文件系统真实路径不一致的操作
/// 防御 Windows 大小写不敏感文件系统导致的误删(如 rm src/ 误删 SRC/)
/// </summary>
public sealed class PathCaseSensitiveGuard
{
    private static readonly FrozenSet<string> DeleteCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "rm", "del", "erase", "Remove-Item", "rmdir", "rd"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 检查命令的路径大小写是否与文件系统一致
    /// </summary>
    /// <param name="command">已解析的 Shell 命令</param>
    /// <param name="resolver">真实路径解析器</param>
    /// <returns>大小写不匹配时返回拦截结果(含真实路径建议);否则放行</returns>
    public PathCaseGuardResult Check(ShellCommand command, IRealPathResolver resolver)
    {
        if (!DeleteCommands.Contains(command.CommandName))
            return PathCaseGuardResult.Pass;

        var targetPath = command.ReferencedPaths.FirstOrDefault();
        if (string.IsNullOrEmpty(targetPath))
            return PathCaseGuardResult.Pass;

        var realPath = resolver.GetRealPath(targetPath);
        if (realPath is null)
            return PathCaseGuardResult.Pass;

        var targetLeaf = GetLeafName(targetPath);
        var realLeaf = GetLeafName(realPath);
        if (string.Equals(targetLeaf, realLeaf, StringComparison.Ordinal))
            return PathCaseGuardResult.Pass;

        var reason = $"路径大小写不匹配:命令使用 '{targetPath}',但文件系统真实路径为 '{realPath}'。已阻止删除以避免大小写不敏感误删,请使用真实路径 '{realPath}' 后再操作。";
        return new PathCaseGuardResult(true, realPath, reason);
    }

    private static string GetLeafName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        return Path.GetFileName(trimmed);
    }
}
