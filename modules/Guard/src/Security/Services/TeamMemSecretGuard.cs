
namespace Core.Security.Services;

/// <summary>
/// 团队记忆密钥守卫实现
/// 对齐 TS: teamMemSecretGuard.ts
/// </summary>
[Register]
public sealed partial class TeamMemSecretGuard : ITeamMemSecretGuard
{
    private readonly string? _teamMemDirectory;

    public TeamMemSecretGuard(string? teamMemDirectory = null)
    {
        _teamMemDirectory = teamMemDirectory;
    }

    /// <inheritdoc />
    public bool IsTeamMemPath(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (string.IsNullOrWhiteSpace(_teamMemDirectory))
            return false;

        // 对齐 TS: isTeamMemPath — resolve 后检查前缀
        var resolvedPath = Path.GetFullPath(filePath);
        var teamDir = Path.GetFullPath(_teamMemDirectory);

        // 确保目录以分隔符结尾，防止前缀攻击（如 /foo/team-evil 不匹配 /foo/team/）
        if (!teamDir.EndsWith(Path.DirectorySeparatorChar))
            teamDir += Path.DirectorySeparatorChar;

        return resolvedPath.StartsWith(teamDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string? CheckTeamMemSecrets(string filePath, string content)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(content);
        // 对齐 TS: feature('TEAMMEM') 守卫 — 如果团队记忆目录未配置，跳过检查
        if (string.IsNullOrWhiteSpace(_teamMemDirectory))
            return null;

        // 对齐 TS: isTeamMemPath(filePath) — 不是团队记忆路径则跳过
        if (!IsTeamMemPath(filePath))
            return null;

        // 对齐 TS: scanForSecrets(content)
        var matches = SecurityPatterns.ScanForSecretMatches(content);
        if (matches.Count == 0)
            return null;

        // 对齐 TS: labels = matches.map(m => m.label).join(', ')
        var labels = string.Join(", ", matches.Select(m => m.Label));
        return $"Content contains potential secrets ({labels}) and cannot be written to team memory. " +
               "Team memory is shared with all repository collaborators. " +
               "Remove the sensitive content and try again.";
    }
}
