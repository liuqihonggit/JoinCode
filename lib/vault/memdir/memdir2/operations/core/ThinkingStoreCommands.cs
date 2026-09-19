namespace Core.Memdir;

/// <summary>
/// 思考记录存储 Actor 命令类型 — 每个命令对应一个 ThinkingStore 写操作，由 ThinkingStoreActor Consumer 串行处理。
/// <para>TASK001: AsyncLock+文件 I/O 迁移到 Actor 邮箱管道，消除显式锁。</para>
/// <para>读操作（GetRecentAsync/GetLatestAsync）不经 Actor，直接读内存 ConcurrentDictionary + lock(entries) 同步锁。</para>
/// </summary>
public abstract record ThinkingStoreCommand;

/// <summary>保存思考记录到文件 — 对应 SaveAsync</summary>
public sealed record ThinkingSaveCmd(
    TaskCompletionSource<Unit> Reply) : ThinkingStoreCommand;
