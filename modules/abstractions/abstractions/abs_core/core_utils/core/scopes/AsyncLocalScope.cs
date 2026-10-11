namespace JoinCode.Abstractions.Utils;

/// <summary>
/// AsyncLocal 作用域 — Enter 时设置值,Dispose 时恢复原值。
/// <para>
/// 用法：<c>using var scope = AsyncLocalScope.Enter(_current, value);</c>
/// 消除手写 <c>var prev = _current.Value; _current.Value = value; try { } finally { _current.Value = prev }</c> 样板。
/// AsyncLocal 基于 ExecutionContext 不可变,using 确保离开作用域时恢复,防止值拘留。
/// </para>
/// </summary>
public sealed class AsyncLocalScope<T> : IDisposable {
    private readonly AsyncLocal<T?> _store;
    private readonly T? _previous;
    private bool _disposed;

    private AsyncLocalScope(AsyncLocal<T?> store, T? previous) {
        _store = store;
        _previous = previous;
    }

    /// <summary>
    /// 设置 AsyncLocal 值,返回作用域(Dispose 时恢复原值)。
    /// </summary>
    /// <param name="store">AsyncLocal 存储</param>
    /// <param name="value">新值</param>
    public static AsyncLocalScope<T> Enter(AsyncLocal<T?> store, T value) {
        ArgumentNullException.ThrowIfNull(store);
        var previous = store.Value;
        store.Value = value;
        return new AsyncLocalScope<T>(store, previous);
    }

    /// <summary>
    /// 恢复 AsyncLocal 原值。幂等。
    /// </summary>
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _store.Value = _previous;
    }
}