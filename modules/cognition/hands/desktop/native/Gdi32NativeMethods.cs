namespace JoinCode.Hands.Desktop.Native;

/// <summary>
/// gdi32.dll P/Invoke 声明 — 截图（BitBlt 兼容位图复制）
/// </summary>
internal static class Gdi32NativeMethods {
    /// <summary>位块传输：将源 DC 的像素复制到目标 DC。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, int rasterOp);

    /// <summary>创建与指定 DC 兼容的内存设备上下文。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    /// <summary>创建与指定 DC 兼容的位图。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    /// <summary>删除逻辑对象（笔/画刷/位图等），释放关联系统资源。</summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    /// <summary>删除设备上下文（DC），释放关联系统资源。</summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    /// <summary>将指定对象选入设备上下文，返回先前同类型对象。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    /// <summary>检索指定设备上下文的能力信息。</summary>
    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    /// <summary>获取指定位图的 DIB 位数据。</summary>
    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, int start, int cLines, IntPtr lpvBits, ref BITMAPINFO lpbi, uint usage);

    /// <summary>创建逻辑画笔（指定样式/宽度/颜色）。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

    /// <summary>创建纯色逻辑画刷。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint crColor);

    /// <summary>绘制矩形边框（使用当前画笔/画刷）。</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Rectangle(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    /// <summary>设置当前绘图模式（光栅操作码）。</summary>
    [DllImport("gdi32.dll")]
    public static extern int SetROP2(IntPtr hdc, int fnDrawMode);

    /// <summary>获取预定义的库存对象（画笔/画刷/字体等）。</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int fnObject);

    /// <summary>将当前位置移动到指定坐标，返回先前位置。</summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr lpPoint);

    /// <summary>从当前位置画线到指定坐标。</summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LineTo(IntPtr hdc, int x, int y);
}
