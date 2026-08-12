using JoinCode.Abstractions.Utils;

namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 代码索引仓库注册表 — 管理多个仓库的 ICodeIndexer 实例
/// 支持多仓库图查询: 每个仓库拥有独立的 InMemoryIndexStore + CodeIndexer
/// 对齐 code-review-graph 的 register/unregister/repos 命令
/// </summary>
public interface ICodeIndexerRegistry : IRegistry
{
    /// <summary>
    /// 注册一个仓库 — 为该仓库创建独立的 ICodeIndexer 实例
    /// </summary>
    Task<RepoRegistration> RegisterAsync(string repoId, string workspaceRoot, CancellationToken ct);

    /// <summary>
    /// 注销一个仓库 — 释放其 ICodeIndexer 实例和相关索引
    /// </summary>
    Task<bool> UnregisterAsync(string repoId, CancellationToken ct);

    /// <summary>
    /// 列出所有已注册的仓库
    /// </summary>
    Task<IReadOnlyList<RepoRegistration>> ListReposAsync(CancellationToken ct);

    /// <summary>
    /// 获取指定仓库的 ICodeIndexer — 不存在则返回 null
    /// </summary>
    ICodeIndexer? GetIndexer(string repoId);

    /// <summary>
    /// 获取默认仓库的 ICodeIndexer — 不存在则返回 null
    /// </summary>
    ICodeIndexer? DefaultIndexer { get; }

    /// <summary>
    /// 仓库注册事件 — 注册成功后触发，订阅方可据此启动 watcher 等附加服务
    /// </summary>
    event EventHandler<RepoRegisteredEventArgs>? RepoRegistered;

    /// <summary>
    /// 仓库注销事件 — 注销成功后触发，订阅方可据此停止 watcher 等附加服务
    /// </summary>
    event EventHandler<RepoUnregisteredEventArgs>? RepoUnregistered;
}

/// <summary>
/// 仓库注册事件参数
/// </summary>
public sealed class RepoRegisteredEventArgs : EventArgs
{
    public required string RepoId { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required ICodeIndexer Indexer { get; init; }
}

/// <summary>
/// 仓库注销事件参数
/// </summary>
public sealed class RepoUnregisteredEventArgs : EventArgs
{
    public required string RepoId { get; init; }
}
