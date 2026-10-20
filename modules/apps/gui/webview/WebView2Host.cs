namespace JoinCode.Gui.WebView;

/// <summary>
/// 把 WebView2 嵌入 Avalonia 视觉树的宿主控件（B2 路线，ADR 0138）：
/// Avalonia 提供原生子窗口（HWND），WebView2 controller 以该 HWND 为父挂载（UseClientArea 自动填充）。
/// 生产移植自 spike 探针（non_deliverables_tools/webview_host_spike），行为经 AOT 冒烟实证。
/// </summary>
internal sealed class WebView2Host : NativeControlHost {
    private readonly string _pageUri;
    private readonly string _userDataDir;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;

    /// <summary>构造宿主控件</summary>
    /// <param name="pageUri">页面 URI（file:// 离线加载）</param>
    /// <param name="userDataDir">WebView2 用户数据目录（用户级，如 ~/.jcc/webview2）</param>
    public WebView2Host(string pageUri, string userDataDir) {
        _pageUri = pageUri;
        _userDataDir = userDataDir;
    }

    /// <summary>WebView2 就绪（controller 挂载完成，导航尚未开始）</summary>
    public event Action<CoreWebView2>? WebViewReady;

    /// <summary>WebView2 初始化失败（激活/环境创建）时触发，宿主窗口据此展示回退提示</summary>
    public event Action<string>? InitFailed;

    /// <summary>收到 web→host 桥接消息（协议见 <see cref="BridgeProtocol"/>，ADR 0141）</summary>
    public event Action<BridgeMessage>? MessageReceived;

    /// <summary>发送 host→web 桥接消息（序列化经 <see cref="BridgeProtocol.Serialize"/> 源生成，AOT 安全）</summary>
    public void Send(BridgeMessage message) {
        _core?.PostWebMessageAsJson(BridgeProtocol.Serialize(message));
    }

    /// <inheritdoc/>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) {
        var handle = base.CreateNativeControlCore(parent);
        _ = InitAsync(handle.Handle);
        return handle;
    }

    /// <summary>创建环境 → 控制器挂载 HWND → 配置 → 触发就绪 → 导航</summary>
    private async Task InitAsync(IntPtr hwnd) {
        try {
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, _userDataDir, null);
            var windowRef = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hwnd);
            _controller = await env.CreateCoreWebView2ControllerAsync(windowRef);
            _controller.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
            var core = _controller.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            // 主题通道提示：宿主变体 → WebView2 首选配色（tokens.css 以 data-jcc-theme 为主通道，此提示为兜底）
            core.Profile.PreferredColorScheme = MapColorScheme(GuiPalette.CurrentVariant);
            core.WebMessageReceived += OnWebMessage;
            _core = core;
            UpdateControllerBounds();
            WebViewReady?.Invoke(core);
            core.Navigate(_pageUri);
        } catch (Exception ex) {
            InitFailed?.Invoke(ex.Message);
        }
    }

    /// <summary>web→host 消息入口：解析成功走 <see cref="MessageReceived"/>；失败 fail-closed 并回错误指引（ADR 0141 决策 4）</summary>
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
        var json = e.WebMessageAsJson;
        if (BridgeProtocol.TryParse(json, out var message)) {
            MessageReceived?.Invoke(message);
            return;
        }
        var preview = json.Length > 120 ? json[..120] : json;
        App.LogDiag($"[WebUI] bridge parse failed: {preview}");
        Send(new AppErrorMessage("桥接消息解析失败",
            "发送方须为 {type, ...fields} 形状且 type 已在 BridgeProtocol.cs 注册（对照 webui/js/bridge.js）"));
    }

    /// <summary>据 GuiPalette 变体映射首选配色：亮系变体（Light/SolarizedLight）→ Light，其余 → Dark</summary>
    private static CoreWebView2PreferredColorScheme MapColorScheme(GuiPalette.GuiThemeVariant variant) =>
        variant is GuiPalette.GuiThemeVariant.Light or GuiPalette.GuiThemeVariant.SolarizedLight
            ? CoreWebView2PreferredColorScheme.Light
            : CoreWebView2PreferredColorScheme.Dark;

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize) {
        var result = base.ArrangeOverride(finalSize);
        UpdateControllerBounds();
        return result;
    }

    /// <summary>控制器挂在原生子窗口上，Bounds 以物理像素计，随 Avalonia 布局同步尺寸</summary>
    private void UpdateControllerBounds() {
        if (_controller is null) {
            return;
        }
        var scale = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        var w = (int)Math.Round(Bounds.Width * scale);
        var h = (int)Math.Round(Bounds.Height * scale);
        if (w <= 0 || h <= 0) {
            return;
        }
        _controller.Bounds = new Windows.Foundation.Rect(0, 0, w, h);
    }

    /// <inheritdoc/>
    protected override void DestroyNativeControlCore(IPlatformHandle control) {
        _controller?.Close();
        _controller = null;
        _core = null;
        base.DestroyNativeControlCore(control);
    }
}
