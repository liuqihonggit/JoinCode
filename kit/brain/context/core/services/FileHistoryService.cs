namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 文件快照，记录某一时刻的文件内容
/// </summary>
public sealed class FileSnapshot {
    /// <summary>
    /// 快照内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 快照生成时间戳
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// 文件路径（已规范化）
    /// </summary>
    public required string FilePath { get; init; }
}

/// <summary>
/// 文件历史服务，按文件路径维护内容快照列表，支持回滚到历史版本
/// <para>使用 Actor 邮箱管道串行化所有操作，消除显式锁 — TASK001</para>
/// </summary>
public sealed class FileHistoryService : IAsyncDisposable {
    private readonly int _maxSnapshots;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly Dictionary<string, List<FileSnapshot>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileHistoryActor _actor;
    private int _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="maxSnapshots">每个文件保留的最大快照数</param>
    /// <param name="clock">时钟服务（可选，默认使用系统时钟）</param>
    public FileHistoryService(IFileSystem fs, int maxSnapshots = 100, IClockService? clock = null) {
        _fs = fs;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSnapshots, 1);
        _maxSnapshots = maxSnapshots;
        _clock = clock ?? SystemClockService.Instance;
        _actor = new FileHistoryActor(this);
    }

    /// <summary>
    /// 跟踪一次文件编辑，记录原始内容快照
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="originalContent">编辑前的原始内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>始终返回 true</returns>
    public async Task<bool> TrackEditAsync(string filePath, string originalContent, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var reply = new TaskCompletionSource<bool>();
        await _actor.SendAsync(new TrackEditCmd(filePath, originalContent, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    private bool TrackEditInternal(string filePath, string originalContent) {
        var normalizedPath = Path.GetFullPath(filePath);

        if (!_history.TryGetValue(normalizedPath, out var snapshots)) {
            snapshots = [];
            _history[normalizedPath] = snapshots;
        }

        snapshots.Add(new FileSnapshot {
            Content = originalContent,
            Timestamp = _clock.GetUtcNow(),
            FilePath = normalizedPath
        });

        while (snapshots.Count > _maxSnapshots) {
            // 从尾部保留 _maxSnapshots 个，避免 RemoveAt(0) 的 O(n) 移动
            var removeCount = snapshots.Count - _maxSnapshots;
            snapshots.RemoveRange(0, removeCount);
        }

        return true;
    }

    /// <summary>
    /// 获取指定文件的所有快照
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>快照列表（按时间升序）；无记录时返回空列表</returns>
    public async Task<IReadOnlyList<FileSnapshot>> GetSnapshotsAsync(string filePath, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var reply = new TaskCompletionSource<IReadOnlyList<FileSnapshot>>();
        await _actor.SendAsync(new GetSnapshotsCmd(filePath, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<FileSnapshot> GetSnapshotsInternal(string filePath) {
        var normalizedPath = Path.GetFullPath(filePath);
        return _history.TryGetValue(normalizedPath, out var snapshots)
            ? snapshots.ToList()
            : [];
    }

    /// <summary>
    /// 回滚指定文件到指定快照版本
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="snapshotIndex">快照索引</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功恢复返回 true；文件无记录、索引越界或文件不存在返回 false</returns>
    public async Task<bool> RestoreAsync(string filePath, int snapshotIndex, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var reply = new TaskCompletionSource<bool>();
        await _actor.SendAsync(new RestoreCmd(filePath, snapshotIndex, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RestoreInternalAsync(string filePath, int snapshotIndex, CancellationToken cancellationToken) {
        var normalizedPath = Path.GetFullPath(filePath);

        if (!_history.TryGetValue(normalizedPath, out var snapshots))
            return false;

        if (snapshotIndex < 0 || snapshotIndex >= snapshots.Count)
            return false;

        var snapshot = snapshots[snapshotIndex];

        if (!_fs.FileExists(normalizedPath))
            return false;

        await _fs.WriteAllTextAsync(normalizedPath, snapshot.Content, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 清除所有文件的历史快照
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ClearAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new ClearCmd(reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    private void ClearInternal()
        => _history.Clear();

    /// <summary>异步释放资源 — await Actor 完全退出</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _actor.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 文件历史 Actor — 串行化所有读写操作，消除显式锁 — TASK001
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class FileHistoryActor : ActorBase<FileHistoryCommand, Unit> {
        private readonly FileHistoryService _owner;

        public FileHistoryActor(FileHistoryService owner) : base() => _owner = owner;

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 FileHistoryService 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        /// <summary>Ask 模式等待回复（无返回值） — 暴露 protected AskAwait 供 FileHistoryService 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(FileHistoryCommand cmd, CancellationToken ct) {
            try {
                switch (cmd) {
                    case TrackEditCmd(var filePath, var content, var reply):
                    reply.SetResult(_owner.TrackEditInternal(filePath, content));
                    break;
                    case GetSnapshotsCmd(var filePath, var reply):
                    reply.SetResult(_owner.GetSnapshotsInternal(filePath));
                    break;
                    case RestoreCmd(var filePath, var snapshotIndex, var reply):
                    reply.SetResult(await _owner.RestoreInternalAsync(filePath, snapshotIndex, ct).ConfigureAwait(false));
                    break;
                    case ClearCmd(var reply):
                    _owner.ClearInternal();
                    reply.SetResult();
                    break;
                }
            } catch (OperationCanceledException) { throw; } catch (Exception ex) {
                switch (cmd) {
                    case TrackEditCmd(_, _, var reply): reply.SetException(ex); break;
                    case GetSnapshotsCmd(_, var reply): reply.SetException(ex); break;
                    case RestoreCmd(_, _, var reply): reply.SetException(ex); break;
                    case ClearCmd(var reply): reply.SetException(ex); break;
                }
            }
        }
    }
}

/// <summary>
/// 文件历史 Actor 命令类型 — 每个命令对应一个 FileHistoryService 操作，由 FileHistoryActor Consumer 串行处理。
/// <para>TASK001: AsyncLock 迁移到 Actor 邮箱管道，消除显式锁。</para>
/// </summary>
public abstract record FileHistoryCommand;

/// <summary>跟踪文件编辑 — 对应 TrackEditAsync</summary>
public sealed record TrackEditCmd(
    string FilePath,
    string Content,
    TaskCompletionSource<bool> Reply) : FileHistoryCommand;

/// <summary>获取文件快照列表 — 对应 GetSnapshotsAsync</summary>
public sealed record GetSnapshotsCmd(
    string FilePath,
    TaskCompletionSource<IReadOnlyList<FileSnapshot>> Reply) : FileHistoryCommand;

/// <summary>回滚文件到指定快照 — 对应 RestoreAsync</summary>
public sealed record RestoreCmd(
    string FilePath,
    int SnapshotIndex,
    TaskCompletionSource<bool> Reply) : FileHistoryCommand;

/// <summary>清除所有历史快照 — 对应 ClearAsync</summary>
public sealed record ClearCmd(
    TaskCompletionSource Reply) : FileHistoryCommand;