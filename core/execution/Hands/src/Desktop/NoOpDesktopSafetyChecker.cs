namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面操作安全检查器 — NoOp 占位实现，总返回安全（测试用；生产用 DesktopSafetyChecker）
/// </summary>
public sealed partial class NoOpDesktopSafetyChecker : ServiceEntity, IDesktopSafetyChecker
{
    /// <summary>检查点击坐标 — NoOp 总返回安全</summary>
    public Task<UnsafeOperationKind> CheckClickAsync(int x, int y, CancellationToken cancellationToken = default)
        => Task.FromResult(UnsafeOperationKind.None);

    /// <summary>检查窗口关闭 — NoOp 总返回安全</summary>
    public Task<UnsafeOperationKind> CheckWindowCloseAsync(IntPtr hWnd, CancellationToken cancellationToken = default)
        => Task.FromResult(UnsafeOperationKind.None);

    protected override void OnDispose()
    {
    }
}
