namespace Core.Utils;

/// <summary>
/// Actor 全局默认配置 — 提供标准回调,命令构造时不传回调则用此默认值。
/// <para><b>双工管道保证</b>:即使调用方不传回调,底层仍用默认回调填充,确保每次通讯双向畅通。</para>
/// <para><b>自定义</b>:命令构造时用 <c>init</c> 属性覆盖默认值,如 <c>new ReadCmd(path) { OnSuccess = ... }</c></para>
/// </summary>
public static class ActorDefaults {
    /// <summary>
    /// 默认失败回调 — 记录到全局错误处理(可替换为日志/监控/断言)。
    /// </summary>
    public static Action<Exception> OnFailure { get; set; } = static _ => { };

    /// <summary>
    /// 默认背压回调 — 空操作(可替换为延迟重试/降速/告警)。
    /// <para>建议替换为 <see cref="ActorBase{TCommand,TOut}.CreateBackpressureHandler"/> 标准背压处理。</para>
    /// </summary>
    public static Action<BackpressureSignal> OnBackpressure { get; set; } = static _ => { };

    /// <summary>
    /// 创建默认成功回调 — 无操作(收到成功回执但不处理结果)。
    /// </summary>
    public static Action<T> DefaultOnSuccess<T>() => static _ => { };
}
