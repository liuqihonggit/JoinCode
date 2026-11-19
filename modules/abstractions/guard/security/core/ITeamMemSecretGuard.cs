namespace JoinCode.Abstractions.Security;

/// <summary>
/// 团队记忆密钥守卫
/// 对齐 TS: teamMemSecretGuard.ts checkTeamMemSecrets
/// 检查写入团队记忆文件的内容是否包含敏感信息（API keys 等）
/// </summary>
public interface ITeamMemSecretGuard {
    /// <summary>
    /// 检查文件路径是否为团队记忆路径
    /// 对齐 TS: isTeamMemPath(filePath)
    /// </summary>
    bool IsTeamMemPath(string filePath);

    /// <summary>
    /// 尝试检测写入团队记忆的内容是否包含密钥
    /// 对齐 TS: checkTeamMemSecrets(filePath, content)
    /// </summary>
    /// <param name="filePath">写入目标文件路径</param>
    /// <param name="content">待检测内容</param>
    /// <param name="violation">检测到密钥时的错误消息</param>
    /// <returns>检测到密钥返回 true；内容安全返回 false</returns>
    bool TryGetTeamMemSecretViolation(string filePath, string content, [NotNullWhen(true)] out string? violation);
}