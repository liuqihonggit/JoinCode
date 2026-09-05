namespace Infrastructure.IO;

/// <summary>
/// 统一持久化管道 — Actor 模型多生产单消费写文件。
/// 继承 <see cref="ActorBase{TCommand}"/> 单消费者 Channel,所有持久化请求串行处理,无需锁。
/// 生产者入队不可变快照(已序列化 JSON),Actor 线程只做 I/O,不访问共享可变状态。
/// 详见 ADR 0068。
/// </summary>
[Register(typeof(IPersistencePipeline), ServiceLifetime.Singleton)]
public sealed class PersistencePipeline : ActorBase<PersistRequest>, IPersistencePipeline
{
    private readonly IFileSystem _fs;
    private readonly ILogger<PersistencePipeline>? _logger;

    /// <summary>
    /// 创建持久化管道 — 有界通道(容量16)+DropOldest 背压。
    /// </summary>
    public PersistencePipeline(IFileSystem fs, ILogger<PersistencePipeline>? logger = null)
        : base(boundedCapacity: 16, fullMode: BoundedChannelFullMode.DropOldest)
    {
        _fs = fs;
        _logger = logger;
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync(PersistRequest request, CancellationToken ct = default)
        => SendAsync(request, ct);

    /// <inheritdoc />
    public bool TryEnqueue(PersistRequest request) => TrySend(request);

    /// <summary>
    /// Actor 消费者处理 — 单线程串行执行,无并发。CreateDirectory + WriteAllTextAsync。
    /// 写完后若 Completion 非空则 TrySetResult,异常时 TrySetException。
    /// </summary>
    protected override async ValueTask HandleAsync(PersistRequest req, CancellationToken ct)
    {
        try
        {
            if (!_fs.DirectoryExists(req.Directory))
            {
                _fs.CreateDirectory(req.Directory);
            }

            var path = _fs.CombinePath(req.Directory, req.FileName);
            await _fs.WriteAllTextAsync(path, req.Content, ct).ConfigureAwait(false);
            _logger?.LogDebug("持久化完成: {Category} -> {Path}", req.Category, path);
            req.Completion?.TrySetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "持久化失败: {Category} -> {Directory}/{FileName}", req.Category, req.Directory, req.FileName);
            req.Completion?.TrySetException(ex);
        }
    }

    /// <inheritdoc />
    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogWarning(ex, "PersistencePipeline 消费者异常");
    }
}
