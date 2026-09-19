namespace Tools.Handlers;

/// <summary>
/// 密钥检测 node — 独立公共对象，包装 <see cref="ITeamMemSecretGuard"/>，提供团队密钥检测能力。
/// 任意工具（文件写入、命令执行等）可注入此 node 检测内容是否包含团队密钥。
/// 返回 null 表示安全，返回错误消息表示检测到密钥。
/// </summary>
[Register(typeof(SecretGuardNode), ServiceLifetime.Singleton)]
public sealed class SecretGuardNode {
    private readonly ITeamMemSecretGuard? _guard;
    private readonly ILogger<SecretGuardNode>? _logger;

    /// <summary>
    /// 构造密钥检测 node
    /// </summary>
    /// <param name="guard">可选的团队密钥守卫</param>
    /// <param name="logger">可选日志记录器</param>
    public SecretGuardNode(
        ITeamMemSecretGuard? guard = null,
        ILogger<SecretGuardNode>? logger = null) {
        _guard = guard;
        _logger = logger;
    }

    /// <summary>
    /// 检查内容是否包含团队密钥。
    /// 对齐 TS: FileWriteTool.ts L157 / FileEditTool.ts L144 — checkTeamMemSecrets。
    /// </summary>
    /// <param name="filePath">文件路径（沙箱解析后）</param>
    /// <param name="content">待检测内容；null 表示跳过（如纯删除行不引入新内容）</param>
    /// <returns>null 表示安全，错误消息表示检测到密钥</returns>
    public string? CheckSecrets(string filePath, string? content) {
        if (content is null || _guard is null)
            return null;

        var error = _guard.CheckTeamMemSecrets(filePath, content);
        if (error is not null)
            _logger?.LogWarning("团队密钥检测命中: {FilePath}", filePath);

        return error;
    }
}