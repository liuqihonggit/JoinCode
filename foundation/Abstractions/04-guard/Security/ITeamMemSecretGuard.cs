namespace JoinCode.Abstractions.Security;

/// <summary>
/// 团队记忆密钥守卫
/// 对齐 TS: teamMemSecretGuard.ts checkTeamMemSecrets
/// 检查写入团队记忆文件的内容是否包含敏感信息（API keys 等）
/// </summary>
public interface ITeamMemSecretGuard
{
    /// <summary>
    /// 检查文件路径是否为团队记忆路径
    /// 对齐 TS: isTeamMemPath(filePath)
    /// </summary>
    bool IsTeamMemPath(string filePath);

    /// <summary>
    /// 检查写入团队记忆的内容是否包含密钥
    /// 对齐 TS: checkTeamMemSecrets(filePath, content)
    /// 返回 null 表示安全，返回错误消息表示检测到密钥
    /// </summary>
    string? CheckTeamMemSecrets(string filePath, string content);
}
