using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
namespace WebViewHostSpike;

/// <summary>
/// 把 WebView2 嵌入 Avalonia 视觉树的宿主控件：
/// Avalonia 提供原生子窗口（HWND），WebView2 controller 以该 HWND 为父挂载（UseClientArea 自动填充）。
/// </summary>
internal sealed class WebView2Host : NativeControlHost
{
    private readonly string _pageUri;
    private readonly string _userDataDir;
    private CoreWebView2Controller? _controller;

    public WebView2Host(string pageUri, string userDataDir)
    {
        _pageUri = pageUri;
        _userDataDir = userDataDir;
    }

    public event Action<CoreWebView2>? WebViewReady;

    /// <summary>WebView2 初始化失败（激活/环境创建）时触发，冒烟模式据此快速失败退出。</summary>
    public event Action<string>? InitFailed;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        _ = InitAsync(handle.Handle);
        return handle;
    }

    private async Task InitAsync(IntPtr hwnd)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, _userDataDir, null);
            var windowRef = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hwnd);
            _controller = await env.CreateCoreWebView2ControllerAsync(windowRef);
            _controller.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
            var core = _controller.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            UpdateControllerBounds();
            WebViewReady?.Invoke(core);
            core.Navigate(_pageUri);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WEBVIEW2-INIT-FAIL: {ex}");
            InitFailed?.Invoke(ex.Message);
        }
    }

    /// <inheritdoc/>
    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        UpdateControllerBounds();
        return result;
    }

    /// <summary>控制器挂在原生子窗口上，Bounds 以物理像素计，随 Avalonia 布局同步尺寸。</summary>
    private void UpdateControllerBounds()
    {
        if (_controller is null)
        {
            return;
        }
        var scale = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        var w = (int)Math.Round(Bounds.Width * scale);
        var h = (int)Math.Round(Bounds.Height * scale);
        if (w <= 0 || h <= 0)
        {
            return;
        }
        _controller.Bounds = new Windows.Foundation.Rect(0, 0, w, h);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _controller?.Close();
        _controller = null;
        base.DestroyNativeControlCore(control);
    }
}
