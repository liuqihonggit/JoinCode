namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// 重定向白名单检测结果
/// </summary>
/// <param name="IsWhitelisted">所有重定向目标都在白名单内</param>
/// <param name="ViolatingTarget">违反白名单的目标（IsWhitelisted=true 时为 null）</param>
/// <param name="NormalizedTarget">规范化后的目标路径（用于诊断）</param>
public sealed record RedirectWhitelistResult(
    bool IsWhitelisted,
    string? ViolatingTarget,
    string? NormalizedTarget);

/// <summary>
/// 重定向白名单 node — 独立公共对象，检测重定向目标是否在白名单内。
/// <para>
/// MTP 扰动纵深防御约束第3条：重定向走白名单，不枚举危险名，只允许工作区路径 + /dev/null。
/// 约束第8条：路径白名单在规范化之后判定（变量/波浪号展开 + normpath）。
/// </para>
/// <para>
/// 白名单规则：
/// <list type="bullet">
/// <item>/dev/null、/dev/stderr、/dev/stdout — 标准丢弃，直接通过</item>
/// <item>工作区内相对/绝对路径 — 规范化后判定 StartsWith(workingDirectory)</item>
/// <item>工作区外路径 — 拒绝（含 ../../etc/passwd、$HOME/.bashrc、nul/NUL/con 等）</item>
/// </list>
/// </para>
/// </summary>
[Register(typeof(RedirectWhitelistNode), ServiceLifetime.Singleton)]
public sealed partial class RedirectWhitelistNode
{
    private static readonly FrozenSet<string> SafeDeviceTargets = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "/dev/null", "/dev/stderr", "/dev/stdout");

    /// <summary>
    /// 匹配文件重定向目标（跳过 fd 重定向如 2&gt;&amp;1）。
    /// <para>
    /// 匹配模式：&gt;file, &gt;&gt;file, 2&gt;file, 2&gt;&gt;file, &amp;&gt;file, &gt;|file 等。
    /// 支持引号包裹：&gt;"file with spaces", &gt;'file'。
    /// 排除 fd 重定向：2&gt;&amp;1, &gt;&amp;2（目标以 &amp; 开头）。
    /// </para>
    /// </summary>
    [GeneratedRegex(@"(?:\d*>>?|&>>?)\s*(?!&)(?:'([^']+)'|""([^""]+)""|(\S+))", RegexOptions.Compiled)]
    private static partial Regex RedirectTargetRegex { get; }

    /// <summary>
    /// 检测命令中的重定向目标是否都在白名单内（工作区路径或 /dev/null）。
    /// </summary>
    /// <param name="command">待检测命令</param>
    /// <param name="workingDirectory">工作目录路径</param>
    /// <returns>白名单检测结果</returns>
    public RedirectWhitelistResult CheckWhitelist(string command, string workingDirectory)
    {
        var targets = ExtractRedirectTargets(command);
        return targets
            .Select(t => EvaluateTarget(t, workingDirectory))
            .FirstOrDefault(r => !r.IsWhitelisted)
            ?? new RedirectWhitelistResult(true, null, null);
    }

    /// <summary>
    /// 提取命令中所有文件重定向目标。
    /// </summary>
    private static IEnumerable<string> ExtractRedirectTargets(string command)
        => RedirectTargetRegex.Matches(command)
            .Select(static m => m.Groups[1].Success ? m.Groups[1].Value
                              : m.Groups[2].Success ? m.Groups[2].Value
                              : m.Groups[3].Value);

    /// <summary>
    /// 评估单个重定向目标是否在白名单内。
    /// </summary>
    private static RedirectWhitelistResult EvaluateTarget(string target, string workingDirectory)
    {
        if (IsSafeDeviceTarget(target))
            return new RedirectWhitelistResult(true, null, null);

        var normalized = NormalizeRedirectTarget(target, workingDirectory);
        return IsWithinWorkspace(normalized, workingDirectory)
            ? new RedirectWhitelistResult(true, null, normalized)
            : new RedirectWhitelistResult(false, target, normalized);
    }

    /// <summary>
    /// 是否为标准设备目标（/dev/null 等）。
    /// </summary>
    private static bool IsSafeDeviceTarget(string target)
        => SafeDeviceTargets.Contains(target);

    /// <summary>
    /// 规范化重定向目标 — 波浪号展开 + Path.GetFullPath（约束第8条）。
    /// </summary>
    private static string NormalizeRedirectTarget(string target, string workingDirectory)
    {
        var expanded = ExpandTilde(target);
        try
        {
            return Path.GetFullPath(expanded, workingDirectory);
        }
        catch (Exception)
        {
            return expanded;
        }
    }

    /// <summary>
    /// 波浪号展开 — ~ 替换为用户主目录。
    /// </summary>
    private static string ExpandTilde(string path)
    {
        if (!path.StartsWith('~'))
            return path;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Length == 1
            ? home
            : home + path[1..];
    }

    /// <summary>
    /// 判断规范化路径是否在工作区内。
    /// </summary>
    private static bool IsWithinWorkspace(string normalizedPath, string workingDirectory)
    {
        try
        {
            var normalizedWorkDir = Path.GetFullPath(workingDirectory);
            return normalizedPath.StartsWith(normalizedWorkDir, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
