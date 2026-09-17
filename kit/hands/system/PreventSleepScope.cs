using System.Threading;
namespace Services.SystemPower;

/// <summary>
/// 防睡眠作用域 — 封装 PreventSleep/AllowSleep 配对,消除散落的 try-finally
/// <para>CreateAsync:调用 PreventSleepAsync,service 为 null 时为 no-op</para>
/// <para>DisposeAsync:调用 AllowSleepAsync</para>
/// <para>DetachTo:将释放绑定到后台 Task(命令完成时 AllowSleep),用于 StartWithBackgroundSupportAsync 等异步场景</para>
/// </summary>
public sealed class PreventSleepScope : IAsyncDisposable
{
    private readonly IPreventSleepService? _service;
    private int _detached;
    private int _disposed;

    private PreventSleepScope(IPreventSleepService? service)
    {
        _service = service;
    }

    /// <summary>
    /// 创建防睡眠作用域 — 调用 PreventSleepAsync,DisposeAsync 时 AllowSleepAsync
    /// </summary>
    public static async Task<PreventSleepScope> CreateAsync(
        IPreventSleepService? service,
        SleepPreventionType type = SleepPreventionType.Continuous,
        CancellationToken cancellationToken = default)
    {
        if (service is not null)
            await service.PreventSleepAsync(type, cancellationToken).ConfigureAwait(false);
        return new PreventSleepScope(service);
    }

    /// <summary>
    /// 将释放绑定到指定 Task — 用于后台命令(命令完成时 AllowSleep)
    /// 调用后 scope 的 DisposeAsync 变为 no-op,由 Task.ContinueWith 接管
    /// </summary>
    public void DetachTo(Task resultTask)
    {
        Interlocked.Exchange(ref _detached, 1);
        _ = resultTask.ContinueWith(async _ => await DisposeAsyncCore().ConfigureAwait(false), TaskScheduler.Default);
    }

    /// <summary>
    /// 异步释放作用域 — 调用 AllowSleepAsync 恢复系统睡眠状态
    /// <para>若已通过 DetachTo 绑定到后台 Task,则本次调用为 no-op</para>
    /// </summary>
    /// <returns>表示异步释放操作的 ValueTask</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _detached, 1) != 0) return default;
        return DisposeAsyncCore();
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_service is not null)
            await _service.AllowSleepAsync().ConfigureAwait(false);
    }
}
