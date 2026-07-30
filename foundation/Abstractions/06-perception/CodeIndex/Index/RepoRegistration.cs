namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 仓库注册信息
/// </summary>
public sealed record RepoRegistration
{
    public required string RepoId { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required DateTimeOffset RegisteredAt { get; init; }
    public required bool IsDefault { get; init; }

    /// <summary>
    /// 文件监听是否已启动
    /// </summary>
    public bool IsWatching { get; init; }
}
