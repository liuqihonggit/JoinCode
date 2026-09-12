namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 引用句柄 — using 模式自动释放引用
/// <para>插件B 引用 插件A 的资源时,获得此句柄,Dispose 时自动减少引用计数</para>
/// <para>解耦设计:持有释放回调 Action,不直接依赖 PluginResourceBase</para>
/// </summary>
public sealed class ResourceReferenceHandle : IDisposable
{
    private Action? _release;
    private bool _disposed;

    /// <summary>创建引用句柄</summary>
    public ResourceReferenceHandle(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        _release = release;
    }

    /// <summary>释放引用 — 调用释放回调,幂等</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var release = _release;
        _release = null;
        release?.Invoke();
    }
}
