namespace JoinCode.Hands.Desktop;

/// <summary>
/// 屏幕截图服务 — GDI BitBlt + GetDIBits + ImageSharp PNG 编码，返回 base64
/// </summary>
[Register(typeof(IScreenCaptureService), ServiceLifetime.Singleton)]
public sealed partial class GdiScreenCaptureService : ServiceEntity, IScreenCaptureService
{
    private readonly ILogger<GdiScreenCaptureService>? _logger;

    public GdiScreenCaptureService(ILogger<GdiScreenCaptureService>? logger = null) => _logger = logger;

    /// <summary>全屏截图</summary>
    public Task<string> CaptureFullScreenAsync(CancellationToken cancellationToken = default)
    {
        var width = User32NativeMethods.GetSystemMetrics(NativeConstants.SM_CXSCREEN);
        var height = User32NativeMethods.GetSystemMetrics(NativeConstants.SM_CYSCREEN);
        return CaptureRegionAsync(0, 0, width, height, cancellationToken);
    }

    /// <summary>指定窗口客户区截图（按窗口矩形从屏幕截取）</summary>
    public Task<string> CaptureWindowAsync(IntPtr hWnd, CancellationToken cancellationToken = default)
    {
        if (!User32NativeMethods.GetWindowRect(hWnd, out var rect))
            return Task.FromResult(string.Empty);
        return CaptureRegionAsync(rect.Left, rect.Top, rect.Width, rect.Height, cancellationToken);
    }

    /// <summary>指定屏幕区域截图</summary>
    public Task<string> CaptureRegionAsync(int x, int y, int width, int height, CancellationToken cancellationToken = default)
    {
        if (width <= 0 || height <= 0) return Task.FromResult(string.Empty);
        return Task.FromResult(CaptureRegionCore(x, y, width, height));
    }

    protected override void OnDispose()
    {
    }

    private string CaptureRegionCore(int x, int y, int width, int height)
    {
        var hdcScreen = User32NativeMethods.GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero) return string.Empty;

        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcMem = Gdi32NativeMethods.CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero) return string.Empty;

            hBitmap = Gdi32NativeMethods.CreateCompatibleBitmap(hdcScreen, width, height);
            if (hBitmap == IntPtr.Zero) return string.Empty;

            hOld = Gdi32NativeMethods.SelectObject(hdcMem, hBitmap);
            if (Gdi32NativeMethods.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, NativeConstants.SRCCOPY) == IntPtr.Zero)
                return string.Empty;

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                    biSizeImage = (uint)(width * height * 4)
                }
            };

            var bytes = new byte[width * height * 4];
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                Gdi32NativeMethods.GetDIBits(hdcMem, hBitmap, 0, height, pinned.AddrOfPinnedObject(), ref bmi, NativeConstants.DIB_RGB_COLORS);
            }
            finally
            {
                pinned.Free();
            }

            for (var i = 3; i < bytes.Length; i += 4) bytes[i] = 255;

            using var image = Image.LoadPixelData<Bgra32>(bytes, width, height);
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "截图失败: ({X},{Y},{W},{H})", x, y, width, height);
            return string.Empty;
        }
        finally
        {
            if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero) Gdi32NativeMethods.SelectObject(hdcMem, hOld);
            if (hBitmap != IntPtr.Zero) Gdi32NativeMethods.DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero) Gdi32NativeMethods.DeleteDC(hdcMem);
            User32NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }
}
