namespace JoinCode.Abstractions.Composition;

/// <summary>
/// 运行期注册句柄 —— DSH <c>ctx.effect()</c> 语义：注册返回 disposer，卸载时逆序回滚。
/// <para>由 <c>NodeRegistry.Register</c> 返回。<see cref="Replace"/> 就地换工厂，
/// <see cref="Dispose"/> 只回滚本句柄对应的那次注册 —— 同一 ID 重复注册时保持栈语义
/// （后注册的先卸载，恢复前一个）。</para>
/// </summary>
public sealed class RegistrationHandle : IDisposable {
    private readonly Action _dispose;
    private readonly Action<Func<IServiceProvider, NodeMetadata?, INode>> _replace;
    private int _disposed;

    /// <summary>构造句柄：注入回滚动作与替换动作。</summary>
    /// <param name="dispose">回滚本次注册的动作。</param>
    /// <param name="replace">就地替换本次注册工厂的动作。</param>
    public RegistrationHandle(
        Action dispose,
        Action<Func<IServiceProvider, NodeMetadata?, INode>> replace) {
        ArgumentNullException.ThrowIfNull(dispose);
        ArgumentNullException.ThrowIfNull(replace);
        _dispose = dispose;
        _replace = replace;
    }

    /// <summary>就地替换该注册项的工厂；原句柄继续有效，Dispose 仍只移除这一项。</summary>
    /// <param name="factory">新的节点工厂。</param>
    /// <exception cref="ObjectDisposedException">句柄已回滚。</exception>
    public void Replace(Func<IServiceProvider, NodeMetadata?, INode> factory) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(factory);
        _replace(factory);
    }

    /// <summary>回滚本次注册（幂等；重复 Dispose 无副作用）。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _dispose();
    }
}
