
namespace Core.Scheduling;

/// <summary>
/// 高水位标记管理器 — IKvStore 增量持久化实现。
/// 单值 KV 存储,PutAsync 天然增量更新,无需全量重写。
/// SemaphoreSlim 保证进程内 IncrementAndGetAsync 读-改-写原子性。
/// </summary>
public sealed class HighWaterMarkManager : IDisposable {
    private readonly IKvStore _kvStore;
    private readonly byte[] _key;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<HighWaterMarkManager>? _logger;

    /// <summary>
    /// 初始化高水位标记管理器
    /// </summary>
    /// <param name="kvStore">KV 存储抽象</param>
    /// <param name="key">高水位标记存储键(UTF-8 编码)</param>
    /// <param name="logger">日志记录器</param>
    public HighWaterMarkManager(IKvStore kvStore, string key, ILogger<HighWaterMarkManager>? logger = null) {
        _kvStore = kvStore ?? throw new ArgumentNullException(nameof(kvStore));
        _key = Encoding.UTF8.GetBytes(key);
        _logger = logger;
    }

    /// <summary>
    /// 读取当前高水位标记值 — 增量读取单个 KV 条目
    /// </summary>
    public async Task<int> ReadAsync(CancellationToken cancellationToken = default) {
        try {
            var value = await _kvStore.GetAsync(_key, cancellationToken).ConfigureAwait(false);
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
        var bytes = BitConverter.GetBytes(newValue);
        await _kvStore.PutAsync(_key, bytes, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[HighWaterMarkManager] 增量更新: {Value}", newValue);
    }

    /// <summary>
    /// 原子递增高水位标记并返回新值 — SemaphoreSlim 保护进程内读-改-写原子性
    /// </summary>
    public async Task<int> IncrementAndGetAsync(CancellationToken cancellationToken = default) {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var current = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var newValue = current + 1;
            await UpdateAsync(newValue, cancellationToken).ConfigureAwait(false);
            return newValue;
        } finally {
            _lock.Release();
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
    /// 释放 SemaphoreSlim 资源 — IKvStore 由 DI 容器管理生命周期
    /// </summary>
    public void Dispose() => _lock.Dispose();
}
