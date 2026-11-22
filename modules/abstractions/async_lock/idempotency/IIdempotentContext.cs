namespace Core.Utils;

/// <summary>
/// 幂等上下文 — 支持幂等守卫中间件的上下文接口。
/// <para>实现此接口的管道上下文可接入 IdempotencyGuardMiddleware 做键控去重。</para>
/// <para><b>IdempotencyKey</b>:None 表示无幂等控制,中间件透传;非空键触发去重逻辑。</para>
/// <para><b>Result</b>:管道完成后由业务中间件设置;幂等命中时由守卫中间件从缓存恢复。</para>
/// </summary>
/// <typeparam name="T">结果类型</typeparam>
public interface IIdempotentContext<T> {
    /// <summary>幂等键 — None 表示无幂等控制(中间件透传),非空键触发去重</summary>
    IdempotencyKey IdempotencyKey { get; }

    /// <summary>操作结果 — 管道完成后由业务中间件设置,幂等命中时由守卫中间件从缓存恢复</summary>
    T? Result { get; set; }
}
