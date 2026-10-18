namespace JoinCode.Gui.WebView;

/// <summary>
/// WebUI 宿主窗口 — 内容区由 WebView2 承载自研 Web UI（B2 路线，ADR 0138）。
/// 由 <c>--webui</c> 启动参数启用；<c>--webui-smoke</c> 为 E2E 冒烟（导航完成即退出，码 0/2/3）。
/// 初始化失败时窗口内展示回退提示（不崩溃），并写入启动诊断日志。
/// </summary>
internal sealed class WebUiWindow : Window {
    private readonly bool _smoke;

    /// <summary>构造 WebUI 窗口</summary>
    /// <param name="pageUri">页面 URI（file:// 离线加载）</param>
    /// <param name="userDataDir">WebView2 用户数据目录</param>
    /// <param name="smoke">冒烟模式：导航完成即退出</param>
    public WebUiWindow(string pageUri, string userDataDir, bool smoke) {
        _smoke = smoke;
        Title = "JoinCode";
        Width = 1440;
        Height = 900;
        ClientSize = new Size(1440, 900);
        var host = new WebView2Host(pageUri, userDataDir);
        host.InitFailed += ShowFallback;
        host.WebViewReady += OnWebViewReady;
        Content = host;
    }

    /// <summary>WebView2 初始化失败回退：窗口内提示 + 冒烟退出码 3</summary>
    private void ShowFallback(string reason) {
        App.LogDiag($"[WebUI] init failed: {reason}");
        Content = new StackPanel {
            Margin = new Thickness(24),
            Spacing = 8,
            Children = {
                new TextBlock { Text = "WebView2 初始化失败，无法加载 WebUI。", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "请安装 Microsoft Edge WebView2 Runtime 后重试。", TextWrapping = TextWrapping.Wrap },
            }
        };
        if (_smoke) {
            Environment.Exit(3);
        }
    }

    /// <summary>WebView2 就绪：写启动诊断标记；冒烟模式盯导航完成后退出</summary>
    private void OnWebViewReady(CoreWebView2 core) {
        App.LogDiag($"[WebUI] WEBVIEW2-READY runtime={core.Environment.BrowserVersionString}");
        if (!_smoke) {
            return;
        }
        core.NavigationCompleted += (_, e) => {
            App.LogDiag($"[WebUI] WEBUI-NAVIGATED success={e.IsSuccess}");
            Environment.Exit(e.IsSuccess ? 0 : 2);
        };
    }
}
