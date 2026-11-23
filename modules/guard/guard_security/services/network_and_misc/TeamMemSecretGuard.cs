
namespace Core.Security.Services;

/// <summary>
/// 团队记忆密钥守卫实现
/// 对齐 TS: teamMemSecretGuard.ts — 写入团队记忆前扫描内容，命中密钥即拒绝。
/// 团队目录来自 <see cref="TeamMemoryLayout"/>（与路径服务同一真相源），
/// 不再依赖外部注入单个团队目录字符串（历史实现恒为 null，导致守卫永不触发）。
/// </summary>
[Register(typeof(ITeamMemSecretGuard), ServiceLifetime.Singleton)]
public sealed partial class TeamMemSecretGuard : ServiceEntity, ITeamMemSecretGuard {
    private readonly string _teamMemoriesRoot;

    /// <summary>
    /// 构造函数 — 按 memdir 存储根解析团队记忆根目录；未提供配置时退回默认 memdir。
    /// </summary>
    /// <param name="config">工作流配置（提供 MemdirPath 覆盖），可选</param>
    public TeamMemSecretGuard(WorkflowConfig? config = null) {
        _teamMemoriesRoot = TeamMemoryLayout.ResolveRoot(
            config?.MemdirPath ?? AppDataConstants.Paths.MemdirDirectory);
    }

    /// <inheritdoc />
    public bool IsTeamMemPath(string filePath) {
        ArgumentNullException.ThrowIfNull(filePath);
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        // 对齐 TS: isTeamMemPath — resolve 后检查前缀
        var resolvedPath = Path.GetFullPath(filePath);

        // 去掉尾分隔符后按「等于根」或「根 + 分隔符」判定，避免 team-memories-evil 这类前缀攻击
        var root = Path.GetFullPath(_teamMemoriesRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return resolvedPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || resolvedPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool TryGetTeamMemSecretViolation(string filePath, string content, [NotNullWhen(true)] out string? violation) {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(content);
        violation = null;

        // 对齐 TS: isTeamMemPath(filePath) — 不是团队记忆路径则跳过
        if (!IsTeamMemPath(filePath))
            return false;

        // 对齐 TS: scanForSecrets(content)
        var matches = SecurityPatterns.ScanForSecretMatches(content);
        if (matches.Count == 0)
            return false;

        // 对齐 TS: labels = matches.map(m => m.label).join(', ')
        // 只回规则名，不回密钥原文 —— 错误信息会进模型上下文与日志
        var labels = string.Join(", ", matches.Select(m => m.RuleId));
        violation = $"Content contains potential secrets ({labels}) and cannot be written to team memory. " +
                    "Team memory is shared with all repository collaborators. " +
                    "Remove the sensitive content and try again.";
        return true;
    }
}
