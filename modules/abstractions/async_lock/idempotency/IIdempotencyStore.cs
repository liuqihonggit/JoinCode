namespace Core.Utils;

/// <summary>
/// 幂等去重存储 — 无锁键控去重，缓存操作结果。
/// <para>用于 Actor 重试场景：重试时携带相同幂等键，接收方守卫层用此存储做去重。</para>
/// <para>实现应使用 ImmutableHamT + CAS 无锁更新，禁止 lock。</para>
/// </summary>
public interface IIdempotencyStore {
    /// <summary>
    /// 尝试注册并缓存结果 — 首次调用返回 true 并缓存结果，重复调用返回 false 不覆盖。
    /// </summary>
    /// <typeparam name="T">结果类型</typeparam>
    /// <param name="key">幂等键</param>
    /// <param name="result">操作结果（仅首次注册时缓存）</param>
    /// <returns>true=首次注册成功；false=键已存在（重复请求）</returns>
    bool TryRegister<T>(IdempotencyKey key, T result);

    /// <summary>
    /// 尝试获取缓存结果。
    /// </summary>
    /// <typeparam name="T">结果类型</typeparam>
    /// <param name="key">幂等键</param>
    /// <param name="result">缓存结果（类型不匹配则为 default）</param>
    /// <returns>true=键存在且类型匹配；false=键不存在或类型不匹配</returns>
    bool TryGetResult<T>(IdempotencyKey key, out T? result);

    /// <summary>
    /// 检查键是否已注册。
    /// </summary>
    bool IsRegistered(IdempotencyKey key);

    /// <summary>
    /// 移除指定键 — TTL 过期或手动清理。
    /// </summary>
    void Evict(IdempotencyKey key);

    /// <summary>
    /// 清除所有已过期的条目 — 惰性 TTL 清理。
    /// </summary>
    /// <param name="ttl">存活时长，条目年龄超过此值则清除</param>
    /// <returns>清除的条目数</returns>
    int EvictExpired(TimeSpan ttl);
}
