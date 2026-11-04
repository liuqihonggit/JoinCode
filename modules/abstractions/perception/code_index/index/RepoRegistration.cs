namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 仓库注册信息
/// </summary>
public sealed record RepoRegistration {
    /// <summary>获取仓库标识。</summary>
    public required string RepoId { get; init; }
    /// <summary>获取工作区根路径。</summary>
    public required string WorkspaceRoot { get; init; }
    /// <summary>获取注册时间。</summary>
    public required DateTimeOffset RegisteredAt { get; init; }
    /// <summary>获取是否为默认仓库。</summary>
    public required bool IsDefault { get; init; }

    /// <summary>
    /// 文件监听是否已启动
    /// </summary>
    public bool IsWatching { get; init; }
}