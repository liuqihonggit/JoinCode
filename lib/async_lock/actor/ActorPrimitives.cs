namespace Core.Utils;

/// <summary>
/// 幂等命令标记接口 — 纯开发规约标记,框架不自动处理重试安全。
/// <para><b>⚠️ 框架行为</b>:ActorBase 不读取此接口,不自动缓存或校验幂等性。重试安全由调用方保证。</para>
/// <para><b>与 IRequestCommand 区别</b>:IRequestCommand 携带幂等键+TryRestoreFromCache,框架 ConsumeLoop 自动做缓存命中跳过;本接口仅为文档标记。</para>
/// <para>典型幂等命令:查询(Get/Read)、取消(Cancel)、状态切换到固定值(SetXxx)。</para>
/// <para>非幂等命令:追加(Append)、递增(Increment)、创建(Create) — 重试可能产生重复副作用。</para>
/// </summary>
public interface IIdempotent { }

/// <summary>
/// 单元类型 — 用于不需要输出的 Actor 的 TOut 参数。
/// </summary>
public readonly record struct Unit {
    /// <summary>唯一实例</summary>
    public static readonly Unit Value = default;
}
