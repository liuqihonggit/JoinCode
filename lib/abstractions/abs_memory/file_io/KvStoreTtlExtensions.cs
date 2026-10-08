namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// IKvStore TTL 扩展 — 任何 IKvStore 消费方都可复用的带过期时间戳缓存读写。
/// <para>value 格式: [8 bytes: 过期时间 UTC Ticks][content bytes]</para>
/// <para>写入时计算 expiry = now + ttl;读取时检查是否过期,过期返回 null(惰性删除)</para>
/// <para>定期清理由 KvStoreTtlCleanupService 负责(扫描+DeleteAsync 过期条目)</para>
/// <para>不带 TTL 的场景直接用原始 PutAsync/GetAsync(无前缀),互不干扰</para>
/// </summary>
public static class KvStoreTtlExtensions {
    private const int TimestampSize = 8;

    /// <summary>
    /// 写入带 TTL 的缓存值 — 前缀 8 字节过期时间戳(UTC Ticks)
    /// </summary>
    /// <param name="store">KV 存储</param>
    /// <param name="key">键</param>
    /// <param name="value">值(内容字节)</param>
    /// <param name="ttl">存活时间(null=永不过期,直接调原始 PutAsync)</param>
    /// <param name="ct">取消令牌</param>
    public static async ValueTask PutWithTtlAsync(
        this IKvStore store, byte[] key, byte[] value, TimeSpan? ttl, CancellationToken ct = default) {
        if (ttl is null) {
            await store.PutAsync(key, value, ct).ConfigureAwait(false);
            return;
        }
        var expiryTicks = DateTime.UtcNow.Add(ttl.Value).Ticks;
        var wrapped = new byte[TimestampSize + value.Length];
        BitConverter.GetBytes(expiryTicks).CopyTo(wrapped, 0);
        value.CopyTo(wrapped, TimestampSize);
        await store.PutAsync(key, wrapped, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取带 TTL 的缓存值 — 检查过期,过期返回 null(不删除,由清理服务负责)
    /// </summary>
    /// <param name="store">KV 存储</param>
    /// <param name="key">键</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>内容字节(未过期),null(不存在或已过期)</returns>
    public static async ValueTask<byte[]?> GetWithTtlAsync(
        this IKvStore store, byte[] key, CancellationToken ct = default) {
        var wrapped = await store.GetAsync(key, ct).ConfigureAwait(false);
        if (wrapped is null || wrapped.Length < TimestampSize) return null;
        if (IsExpired(wrapped)) return null;
        return wrapped[TimestampSize..];
    }

    /// <summary>
    /// 检查 wrapped value 是否已过期(前 8 字节时间戳 vs 当前时间)
    /// </summary>
    /// <param name="wrappedValue">带时间戳的值(从 ScanAsync 获取)</param>
    /// <param name="now">当前时间(null=DateTime.UtcNow,测试可注入)</param>
    /// <returns>true=已过期,false=未过期或无时间戳</returns>
    public static bool IsExpired(byte[] wrappedValue, DateTime? now = null) {
        if (wrappedValue.Length < TimestampSize) return false;
        var expiryTicks = BitConverter.ToInt64(wrappedValue, 0);
        if (expiryTicks < DateTime.MinValue.Ticks || expiryTicks > DateTime.MaxValue.Ticks) return true;
        var expiry = new DateTime(expiryTicks, DateTimeKind.Utc);
        return (now ?? DateTime.UtcNow) > expiry;
    }

    /// <summary>
    /// 从 wrapped value 中提取内容(跳过前 8 字节时间戳)
    /// </summary>
    public static byte[] Unwrap(byte[] wrappedValue) => wrappedValue[TimestampSize..];
}
