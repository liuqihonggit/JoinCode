namespace Core.Memdir;

/// <summary>
/// 思考记录存储 Actor 命令类型 — 每个命令对应一个 ThinkingStore 写操作，由 ThinkingStoreActor Consumer 串行处理。
/// <para>TASK001: AsyncLock+文件 I/O 迁移到 Actor 邮箱管道，消除显式锁。</para>
/// <para>读操作（GetRecentAsync/GetLatestAsync）不经 Actor，直接读内存 ConcurrentDictionary + lock(entries) 同步锁。</para>
/// </summary>
public abstract record ThinkingStoreCommand;

/// <summary>保存思考记录到文件 — 对应 SaveAsync</summary>
public sealed record ThinkingSaveCmd(
    IdempotencyKey IdempotencyKey) : ThinkingStoreCommand, IRequestCommand {
    /// <summary>回复通道 — Consumer 处理完成后写入结果，调用方通过 Reader.ReadAsync 拉取</summary>
    public Channel<Unit> ReplyChannel { get; } = Channel.CreateUnbounded<Unit>();

    /// <summary>从幂等缓存恢复结果 — 命中缓存时写入 ReplyChannel 并返回 true</summary>
    public bool TryRestoreFromCache(IIdempotencyStore store) {
        if (store.TryGetResult<Unit>(IdempotencyKey, out var cached)) {
            ReplyChannel.Writer.TryWrite(cached);
            return true;
        }
        return false;
    }
}