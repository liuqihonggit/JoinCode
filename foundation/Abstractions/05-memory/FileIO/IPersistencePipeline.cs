namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 统一持久化管道 — Actor 模型多生产单消费写文件。
/// <para>所有状态型分类(code_index/memory/permission/task/notebook/structured_output)的持久化
/// 统一通过此管道入队,Actor 单线程串行写磁盘,无锁无并发问题。</para>
/// <para>生产者在主线程构造不可变快照(已序列化的 JSON 字符串)后入队,
/// Actor 线程只做 I/O(CreateDirectory + WriteAllTextAsync),不访问共享可变状态。</para>
/// <para>详见 ADR [0068](docs/adr/0068-unified-persistence-pipeline-actor.md)</para>
/// </summary>
public interface IPersistencePipeline : IAsyncDisposable
{
    /// <summary>
    /// 异步入队持久化请求 — 有界通道在满时按 DropOldest 丢弃最旧请求。
    /// </summary>
    ValueTask EnqueueAsync(PersistRequest request, CancellationToken ct = default);

    /// <summary>
    /// 同步尝试入队 — 通道已关闭、已释放或已满时返回 false。
    /// </summary>
    bool TryEnqueue(PersistRequest request);
}

/// <summary>
/// 持久化请求 — 不可变快照,生产者已序列化为 JSON 字符串。
/// </summary>
public sealed class PersistRequest
{
    /// <summary>分类标识("code_index"/"memory"/"permission"/...),用于日志和监控</summary>
    public required string Category { get; init; }
    /// <summary>目标目录(如 ".jcc/code-index")</summary>
    public required string Directory { get; init; }
    /// <summary>文件名(如 "code-index.json")</summary>
    public required string FileName { get; init; }
    /// <summary>已序列化的 JSON 内容</summary>
    public required string Content { get; init; }
    /// <summary>可选:需要等待写完的生产者传入,Actor 写完后 TrySetResult;异常时 TrySetException。null 表示 fire-and-forget。</summary>
    public TaskCompletionSource? Completion { get; init; }
}
