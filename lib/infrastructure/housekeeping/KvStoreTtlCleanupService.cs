namespace Infrastructure.Housekeeping;

/// <summary>
/// KV 存储 TTL 定期清理服务 — 扫描指定前缀的 key,删除已过期的条目(带 8 字节时间戳前缀的 value)。
/// <para>默认每 24 小时执行一次,首次延迟 10 分钟。扫描前缀由 KeyPrefixes 指定(如 ["gh:"])。</para>
/// <para>过期判定: KvStoreTtlExtensions.IsExpired(value) — 前 8 字节 UTC Ticks vs 当前时间。</para>
/// <para>清理方式: DeleteAsync(key) — 写入墓碑标记,LSM 压实时物理删除。</para>
/// <para>注册: [Register(typeof(IHostedService), ServiceLifetime.Singleton)] — 由源码生成器自动扫描</para>
/// </summary>
[Register(typeof(IHostedService), ServiceLifetime.Singleton)]
public sealed class KvStoreTtlCleanupService : PeriodicBackgroundServiceBase {
    private readonly IKvStore _kvStore;
    private readonly IClockService _clock;
    private readonly ILogger<KvStoreTtlCleanupService>? _logger;

    /// <inheritdoc/>
    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(30);
    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromHours(24);
    /// <inheritdoc/>
    protected override IClockService Clock => _clock;
    /// <inheritdoc/>
    protected override ILogger? Logger => _logger;
    /// <inheritdoc/>
    protected override string ServiceName => "KV存储TTL清理";

    /// <summary>要扫描清理的 key 前缀列表(如 ["gh:"] — 扫描所有 gh: 开头的缓存 key)</summary>
    internal static readonly string[] KeyPrefixes = ["gh:"];

    /// <summary>
    /// 构造函数 — 注入 KV 存储、时钟、日志
    /// </summary>
    public KvStoreTtlCleanupService(
        IKvStore kvStore,
        IClockService clock,
        ILogger<KvStoreTtlCleanupService>? logger = null) {
        _kvStore = kvStore;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken) {
        var now = _clock.GetUtcNow();
        var totalDeleted = 0;

        foreach (var prefix in KeyPrefixes) {
            totalDeleted += await CleanupPrefixAsync(prefix, now, cancellationToken).ConfigureAwait(false);
        }

        if (totalDeleted > 0) {
            _logger?.LogDebug("KV存储TTL清理完成@{Time}: 删除 {Count} 个过期条目", now.ToString("yyyy-MM-dd"), totalDeleted);
        }
    }

    /// <summary>
    /// 清理指定前缀的过期条目 — 范围扫描 [prefix, prefixNext),逐条检查时间戳
    /// </summary>
    private async Task<int> CleanupPrefixAsync(string prefix, DateTime now, CancellationToken ct) {
        var fromBytes = Encoding.UTF8.GetBytes(prefix);
        var toBytes = GetPrefixUpperBound(prefix);

        var deleted = 0;
        await foreach (var (key, value) in _kvStore.ScanAsync(from: fromBytes, to: toBytes, ct: ct).ConfigureAwait(false)) {
            if (KvStoreTtlExtensions.IsExpired(value, now)) {
                await _kvStore.DeleteAsync(key, ct).ConfigureAwait(false);
                deleted++;
            }
        }
        return deleted;
    }

    /// <summary>
    /// 计算前缀的上界(用于范围扫描的 to 参数) — 将前缀最后一个字节 +1
    /// <para>如 "gh:" → "gh;" (ASCII ':'=G3A → ';'G3B),覆盖所有 "gh:" 开头的 key</para>
    /// </summary>
    private static byte[] GetPrefixUpperBound(string prefix) {
        var bytes = Encoding.UTF8.GetBytes(prefix);
        if (bytes.Length == 0) return [];
        var result = (byte[])bytes.Clone();
        result[^1]++;
        return result;
    }
}
