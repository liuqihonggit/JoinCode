namespace JoinCode.Hands.Desktop.Native;

/// <summary>
/// GDI SelectObject 作用域 — Dispose 时恢复原对象,消除手动 SelectObject(hdc, oldObj) 样板
/// </summary>
internal sealed class GdiSelectScope : IDisposable
{
    private readonly IntPtr _hdc;
    private readonly IntPtr _hOld;
    private bool _disposed;

    /// <summary>
    /// 选择 GDI 对象到设备上下文,返回作用域(Dispose 时恢复原对象)
    /// </summary>
    public GdiSelectScope(IntPtr hdc, IntPtr hgdiobj)
    {
        _hdc = hdc;
        _hOld = Gdi32NativeMethods.SelectObject(hdc, hgdiobj);
    }

    /// <summary>恢复原 GDI 对象</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hOld != IntPtr.Zero)
            Gdi32NativeMethods.SelectObject(_hdc, _hOld);
    }
}
