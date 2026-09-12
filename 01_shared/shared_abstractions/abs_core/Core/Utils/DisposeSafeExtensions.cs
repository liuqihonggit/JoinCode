namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 资源释放安全扩展 — 消除 Dispose 样板代码（try-catch ObjectDisposedException）。
/// <para>
/// 用法：<c>x.DisposeSafe(_logger)</c> 替代 <c>try { x.Dispose(); } catch (ObjectDisposedException) { ... }</c>。
/// 详见 ADR-0093、AGENTS.md「代码风格规范」。
/// </para>
/// </summary>
public static class DisposeSafeExtensions
{
    /// <summary>
    /// 安全释放 <see cref="IDisposable"/> 对象：吞 <see cref="ObjectDisposedException"/>（幂等），其他异常可选记录日志。
    /// </summary>
    /// <param name="obj">待释放对象，可为 null（no-op）。</param>
    /// <param name="logger">可选日志器，非 ObjectDisposedException 的异常会记 Warning。</param>
    /// <param name="caller">调用方成员名（自动填充），用于日志定位。</param>
    public static void DisposeSafe(
        this IDisposable? obj,
        ILogger? logger = null,
        [CallerMemberName] string? caller = null)
    {
        if (obj is null) return;
        try { obj.Dispose(); }
        catch (ObjectDisposedException ex) { logger?.LogDebug(ex, "[{Caller}] 对象已释放，幂等忽略", caller); }
        catch (Exception ex) { logger?.LogWarning(ex, "[{Caller}] Dispose 失败", caller); }
    }

    /// <summary>
    /// 安全异步释放 <see cref="IAsyncDisposable"/> 对象：吞 <see cref="ObjectDisposedException"/>，其他异常可选记录日志。
    /// </summary>
    /// <param name="obj">待释放对象，可为 null（no-op）。</param>
    /// <param name="logger">可选日志器。</param>
    /// <param name="caller">调用方成员名（自动填充）。</param>
    public static async ValueTask DisposeSafeAsync(
        this IAsyncDisposable? obj,
        ILogger? logger = null,
        [CallerMemberName] string? caller = null)
    {
        if (obj is null) return;
        try { await obj.DisposeAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException ex) { logger?.LogDebug(ex, "[{Caller}] 对象已释放，幂等忽略", caller); }
        catch (Exception ex) { logger?.LogWarning(ex, "[{Caller}] DisposeAsync 失败", caller); }
    }

    /// <summary>
    /// 安全取消并释放 <see cref="CancellationTokenSource"/>：先 Cancel 再 Dispose，全程吞 <see cref="ObjectDisposedException"/>。
    /// </summary>
    /// <param name="cts">待取消并释放的源，可为 null（no-op）。</param>
    /// <param name="logger">可选日志器。</param>
    /// <param name="caller">调用方成员名（自动填充）。</param>
    public static void CancelAndDisposeSafe(
        this CancellationTokenSource? cts,
        ILogger? logger = null,
        [CallerMemberName] string? caller = null)
    {
        if (cts is null) return;
        try { cts.Cancel(); }
        catch (ObjectDisposedException ex) { logger?.LogDebug(ex, "[{Caller}] 对象已释放，幂等忽略", caller); }
        catch (Exception ex) { logger?.LogWarning(ex, "[{Caller}] Cancel 失败", caller); }
        cts.DisposeSafe(logger, caller);
    }
}
