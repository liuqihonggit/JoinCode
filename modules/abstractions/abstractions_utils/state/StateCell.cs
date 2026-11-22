namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 不可变状态单元 — 单枚举字段的 CAS 发布载体。
/// <para>用引用交换实现原子状态迁移（Interlocked.CompareExchange），替代锁保护的枚举字段：
/// 读侧 Volatile.Read 零阻塞，写侧 CAS 失败重试。</para>
/// </summary>
/// <typeparam name="TState">状态枚举类型</typeparam>
/// <param name="value">状态值</param>
internal sealed class StateCell<TState>(TState value) where TState : struct, Enum {
    /// <summary>状态值（单元创建后不可变）。</summary>
    public TState Value { get; } = value;
}
