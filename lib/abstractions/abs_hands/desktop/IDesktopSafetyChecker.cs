namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 桌面操作安全检查器 — 撤销元意识（PRD U-01~U-04）的执行前护栏
/// </summary>
public interface IDesktopSafetyChecker
{
    /// <summary>检查鼠标点击坐标是否命中危险区域（如"确定删除"按钮）</summary>
    Task<UnsafeOperationKind> CheckClickAsync(int x, int y, CancellationToken cancellationToken = default);

    /// <summary>检查关闭窗口是否可能导致未保存数据丢失</summary>
    Task<UnsafeOperationKind> CheckWindowCloseAsync(IntPtr hWnd, CancellationToken cancellationToken = default);
}
