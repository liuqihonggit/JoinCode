namespace JoinCode.Abstractions.Utils;

/// <summary>
/// TaskCompletionSource 工厂 — 统一使用 RunContinuationsAsynchronously 避免同步续体死锁。
/// <para>
/// 用法：<c>var tcs = TcsFactory.Create&lt;T&gt;();</c>
/// 消除各处手写 <c>new TaskCompletionSource&lt;T&gt;(TaskCreationOptions.RunContinuationsAsynchronously)</c> 样板。
/// RunContinuationsAsynchronously 让续体在线程池上执行而非同步调用,避免 Actor/邮箱场景下 Consumer 线程被阻塞。
/// </para>
/// </summary>
public static class TcsFactory {
    /// <summary>创建带 RunContinuationsAsynchronously 的 TaskCompletionSource&lt;T&gt;</summary>
    public static TaskCompletionSource<T> Create<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>创建带 RunContinuationsAsynchronously 的 TaskCompletionSource</summary>
    public static TaskCompletionSource Create() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}