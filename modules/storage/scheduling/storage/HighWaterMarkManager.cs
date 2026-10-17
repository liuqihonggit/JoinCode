
// JCC9203 抑制: 存量释放函数超时, 后续异步化改造
#pragma warning disable JCC9203
namespace Core.Scheduling;

/// <summary>
/// 高水位标记管理器 — IKvStore 增量持久化实现。
/// 单值 KV 存储,PutAsync 天然增量更新,无需全量重写。
/// SemaphoreSlim(1,1) 串行化 IncrementAndGetAsync 读-改-写原子性 — 不可重入,同线程第二次获取阻塞(防重入)。
/// </summary>
public sealed class HighWaterMarkManager : IDisposable {
    private readonly IKvStoreLeaseFactory _factory;
    private readonly byte[] _key;
    private readonly SemaphoreSlim _incrementLock = new(1, 1);
    private readonly ILogger<HighWaterMarkManager>? _logger;
    private volatile bool _disposed;

    /// <summary>
    /// 初始化高水位标记管理器
    /// </summary>
    /// <param name="factory">KV 存储租约工厂</param>
    /// <param name="key">高水位标记存储键(UTF-8 编码)</param>
    /// <param name="logger">日志记录器</param>
    public HighWaterMarkManager(IKvStoreLeaseFactory factory, string key, ILogger<HighWaterMarkManager>? logger = null) {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _key = Encoding.UTF8.GetBytes(key);
        _logger = logger;
    }

    /// <summary>
    /// 读取当前高水位标记值 — 增量读取单个 KV 条目
    /// </summary>
    public async Task<int> ReadAsync(CancellationToken cancellationToken = default) {
        using var lease = _factory.Acquire();
        return await ReadCoreAsync(lease, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ReadCoreAsync(IKvStoreLease lease, CancellationToken ct) {
        try {
            var value = await lease.GetAsync(_key, ct).ConfigureAwait(false);
            if (value is null || value.Length < 4)
                return 0;
            return BitConverter.ToInt32(value);
        } catch {
            return 0;
        }
    }

    /// <summary>
    /// 增量更新高水位标记值 — PutAsync 单条写入,LSM-Tree WAL+MemTable 增量持久化
    /// </summary>
    /// <param name="newValue">新值</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task UpdateAsync(int newValue, CancellationToken cancellationToken = default) {
        using var lease = _factory.Acquire();
        await UpdateCoreAsync(lease, newValue, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateCoreAsync(IKvStoreLease lease, int newValue, CancellationToken ct) {
        var bytes = BitConverter.GetBytes(newValue);
        await lease.PutAsync(_key, bytes, ct).ConfigureAwait(false);
        _logger?.LogDebug("[HighWaterMarkManager] 增量更新: {Value}", newValue);
    }

    /// <summary>
    /// 原子递增高水位标记并返回新值 — SemaphoreSlim(1,1) 串行化读-改-写原子性,不可重入
    /// </summary>
    public async Task<int> IncrementAndGetAsync(CancellationToken cancellationToken = default) {
        if (_disposed) throw new ObjectDisposedException(nameof(HighWaterMarkManager));
        cancellationToken.ThrowIfCancellationRequested();
        await _incrementLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            using var lease = _factory.Acquire();
            var current = await ReadCoreAsync(lease, cancellationToken).ConfigureAwait(false);
            var newValue = current + 1;
            await UpdateCoreAsync(lease, newValue, cancellationToken).ConfigureAwait(false);
            return newValue;
        } finally {
            _incrementLock.Release();
        }
    }

    /// <summary>
    /// 从高水位标记生成任务ID字符串
    /// </summary>
    public static string GenerateTaskId(int value) {
        return value.ToString("D4");
    }

    /// <summary>
    /// 从任务ID解析整数值
    /// </summary>
    public static int ParseTaskId(string taskId) {
        if (int.TryParse(taskId, out var value)) {
            return value;
        }
        return 0;
    }

    /// <summary>
    /// 释放 SemaphoreSlim;幂等 — IKvStore 由 DI 容器管理生命周期
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, true)) return;
        _incrementLock.DisposeSafe();
    }
}
