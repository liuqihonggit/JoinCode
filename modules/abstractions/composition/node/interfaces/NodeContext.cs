namespace JoinCode.Abstractions.Composition;

/// <summary>
/// 可逆注册上下文 — 节点注册时声明 cleanup，卸载时自动逆序回滚
/// <para>类似 DSH 的 ctx.effect()：正向操作 + cleanup 委托</para>
/// <para>UnloadAsync 逆序执行所有 cleanup，异常不传播（通过可选回调上报）</para>
/// </summary>
public sealed class NodeContext : IAsyncDisposable {
    private readonly ConcurrentStack<Func<ValueTask>> _cleanupStack = new();
    private readonly IServiceProvider _services;
    private readonly Action<Exception, string?>? _onCleanupFailed;
    private volatile bool _disposed;

    /// <summary>已登记副作用数量</summary>
    public int RegisteredCount => _cleanupStack.Count;

    /// <summary>创建可逆注册上下文</summary>
    /// <param name="services">DI 容器</param>
    /// <param name="onCleanupFailed">cleanup 异常上报回调（null 时静默吞）</param>
    public NodeContext(IServiceProvider services, Action<Exception, string?>? onCleanupFailed = null) {
        _services = services;
        _onCleanupFailed = onCleanupFailed;
    }

    /// <summary>从 DI 容器获取服务</summary>
    public T GetService<T>() where T : notnull => _services.GetRequiredService<T>();

    /// <summary>
    /// 登记异步可逆副作用 — 返回 value 便于链式调用
    /// <para>cleanup 在 UnloadAsync 时逆序执行</para>
    /// </summary>
    public T RegisterEffect<T>(T value, Func<ValueTask> cleanup) {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cleanup);
        _cleanupStack.Push(cleanup);
        return value;
    }

    /// <summary>
    /// 登记同步可逆副作用 — 返回 value 便于链式调用
    /// <para>cleanup 在 UnloadAsync 时逆序执行</para>
    /// </summary>
    public T RegisterEffect<T>(T value, Action cleanup) {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cleanup);
        _cleanupStack.Push(() => { cleanup(); return ValueTask.CompletedTask; });
        return value;
    }

    /// <summary>
    /// 卸载 — 逆序执行所有 cleanup，异常不传播
    /// <para>幂等：已卸载时立即返回</para>
    /// </summary>
    public async ValueTask UnloadAsync() {
        if (_disposed) return;
        _disposed = true;

        while (_cleanupStack.TryPop(out var cleanup)) {
            try {
                await cleanup().ConfigureAwait(false);
            } catch (Exception ex) {
                ReportCleanupFailed(ex);
            }
        }
    }

    /// <summary>GCGC — 等同 UnloadAsync</summary>
    public ValueTask DisposeAsync() => UnloadAsync();

    private void ThrowIfDisposed() {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NodeContext),
                "NodeContext 已卸载；不能在卸载阶段继续登记副作用。");
    }

    private void ReportCleanupFailed(Exception ex) {
        try {
            _onCleanupFailed?.Invoke(ex, null);
        } catch (Exception callbackEx) {
            Console.Error.WriteLine($"[NodeContext] onCleanupFailed callback error: {callbackEx.Message}");
        }
    }
}
