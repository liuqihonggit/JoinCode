
namespace Core.Scheduling;

/// <summary>
/// 高水位标记管理器 — IKvStore 增量持久化实现。
/// 单值 KV 存储,PutAsync 天然增量更新,无需全量重写。
/// Actor 模式 — 内部组合 HwmActor(继承 ActorBase)串行化 IncrementAndGetAsync 读-改-写原子性,获得监督/背压/生命周期/错误恢复。
/// </summary>
public sealed class HighWaterMarkManager : IAsyncDisposable {
    private readonly IKvStore _kvStore;
    private readonly byte[] _key;
    private readonly HwmActor _actor;
    private readonly ILogger<HighWaterMarkManager>? _logger;
    private volatile bool _disposed;

    /// <summary>
    /// 内部 Actor — 继承 ActorBase,Consumer 线程独占 IncrementAndGetAsync 串行化
    /// </summary>
    private sealed class HwmActor(HighWaterMarkManager owner, ILogger? logger)
        : ActorBase<HwmActor.Command, Unit>(logger: logger) {

        /// <summary>命令 — 携带异步操作委托和回复通道</summary>
        public sealed class Command(
            Func<HighWaterMarkManager, CancellationToken, Task<int>> execute,
            TaskCompletionSource<int> tcs,
            CancellationToken ct) {
            public readonly Func<HighWaterMarkManager, CancellationToken, Task<int>> Execute = execute;
            public readonly TaskCompletionSource<int> Tcs = tcs;
            public readonly CancellationToken Ct = ct;
        }

        protected override void Handle(Command command, CancellationToken ct) {
            RegisterInFlight(ExecuteAndSetResultAsync(command));
        }

        private async Task ExecuteAndSetResultAsync(Command command) {
            try {
                var result = await command.Execute(owner, command.Ct).ConfigureAwait(false);
                command.Tcs.TrySetResult(result);
            } catch (OperationCanceledException) {
                command.Tcs.TrySetCanceled();
            } catch (Exception ex) {
                command.Tcs.TrySetException(ex);
            }
        }
    }

    /// <summary>
    /// 初始化高水位标记管理器 — 启动内部 Actor
    /// </summary>
    /// <param name="kvStore">KV 存储抽象</param>
    /// <param name="key">高水位标记存储键(UTF-8 编码)</param>
    /// <param name="logger">日志记录器</param>
    public HighWaterMarkManager(IKvStore kvStore, string key, ILogger<HighWaterMarkManager>? logger = null) {
        _kvStore = kvStore ?? throw new ArgumentNullException(nameof(kvStore));
        _key = Encoding.UTF8.GetBytes(key);
        _logger = logger;
        _actor = new HwmActor(this, logger);
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
    /// 原子递增高水位标记并返回新值 — Actor 串行化读-改-写原子性
    /// </summary>
    public async Task<int> IncrementAndGetAsync(CancellationToken cancellationToken = default) {
        if (_disposed) throw new ObjectDisposedException(nameof(HighWaterMarkManager));
        cancellationToken.ThrowIfCancellationRequested();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        if (!_actor.TrySend(new HwmActor.Command(
            (self, ct) => self.IncrementAndGetCoreAsync(ct),
            tcs, cancellationToken)))
            throw new ObjectDisposedException(nameof(HighWaterMarkManager));
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>IncrementAndGetAsync 核心 — 在 Actor Consumer 线程执行</summary>
    private async Task<int> IncrementAndGetCoreAsync(CancellationToken ct) {
        var current = await ReadAsync(ct).ConfigureAwait(false);
        var newValue = current + 1;
        await UpdateAsync(newValue, ct).ConfigureAwait(false);
        return newValue;
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
    /// 异步释放 Actor;幂等 — IKvStore 由 DI 容器管理生命周期
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, true)) return;
        await _actor.DisposeAsync().ConfigureAwait(false);
    }
}
