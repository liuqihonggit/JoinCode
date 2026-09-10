namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 副作用作用域 — apply/revert 对 + 逆序回滚 + revert 异常上报(ADR 0098)
/// <para>同步撤销链 Stack&lt;(Action Revert, string? Desc)&gt;,DisposeAsync 逆序 Pop</para>
/// <para>异步撤销链 Stack&lt;IAsyncDisposable&gt;,DisposeAsync 先于同步链执行</para>
/// <para>revert 异常通过 onRevertFailed 回调上报,不静默吞掉</para>
/// </summary>
public sealed class EffectScope : IAsyncDisposable
{
    private readonly Stack<(Action Revert, string? Description)> _undo = new();
    private readonly Stack<IAsyncDisposable> _asyncUndo = new();
    private readonly Action<Exception, string?>? _onRevertFailed;
    private bool _disposed;

    /// <summary>已登记副作用数量(同步+异步)</summary>
    public int RegisteredCount { get; private set; }

    /// <summary>创建副作用作用域</summary>
    /// <param name="onRevertFailed">revert 异常上报回调(null 时静默吞,不推荐)</param>
    public EffectScope(Action<Exception, string?>? onRevertFailed = null)
    {
        _onRevertFailed = onRevertFailed;
    }

    /// <summary>
    /// 登记同步副作用 — apply 立即执行,revert 加入撤销链
    /// </summary>
    /// <param name="apply">立即执行的副作用</param>
    /// <param name="revert">撤销操作(不能为 null,无需撤销传 () =&gt; { })</param>
    /// <param name="description">撤销描述(用于诊断)</param>
    public void Add(Action apply, Action revert, string? description = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EffectScope),
                "EffectScope 已释放;不能在卸载阶段继续登记副作用。");
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(revert);

        apply();
        _undo.Push((revert, description));
        RegisteredCount++;
    }

    /// <summary>
    /// 登记异步副作用 — IAsyncDisposable 加入异步撤销链
    /// <para>DisposeAsync 时逆序 await DisposeAsync,先于同步撤销链</para>
    /// </summary>
    public void AddAsync(IAsyncDisposable disposable)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EffectScope));
        ArgumentNullException.ThrowIfNull(disposable);
        _asyncUndo.Push(disposable);
        RegisteredCount++;
    }

    /// <summary>
    /// 异步释放 — 先逆序 await 异步撤销链,再逆序执行同步撤销链
    /// <para>幂等:已释放时立即返回</para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisposeAsyncChainAsync().ConfigureAwait(false);
        DisposeSyncChain();
    }

    private async ValueTask DisposeAsyncChainAsync()
    {
        while (_asyncUndo.Count > 0)
        {
            var d = _asyncUndo.Pop();
            try { await d.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { ReportRevertFailed(ex, "异步撤销"); }
        }
    }

    private void DisposeSyncChain()
    {
        while (_undo.Count > 0)
        {
            var (revert, desc) = _undo.Pop();
            try { revert(); }
            catch (Exception ex) { ReportRevertFailed(ex, desc); }
        }
    }

    private void ReportRevertFailed(Exception ex, string? desc)
    {
        try { _onRevertFailed?.Invoke(ex, desc); }
        catch (Exception callbackEx)
        {
            Console.WriteLine($"[EffectScope] onRevertFailed 回调异常: {callbackEx.Message}");
        }
    }
}
