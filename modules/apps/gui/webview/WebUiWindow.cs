namespace JoinCode.Gui.WebView;

/// <summary>
/// WebUI 宿主窗口 — 内容区由 WebView2 承载自研 Web UI（B2 路线，ADR 0138）。
/// 由 <c>--webui</c> 启动参数启用；<c>--webui-smoke</c> 为 E2E 冒烟：
/// 导航成功 → 等 web/ready 握手 → smoke/ping·smoke/pong 往返（5s 看门狗）。
/// 退出码：0 全通 / 2 导航失败 / 3 初始化失败 / 4 桥接超时或 nonce 不符（ADR 0141 决策 5）。
/// 初始化失败时窗口内展示回退提示（不崩溃），并写入启动诊断日志。
/// </summary>
internal sealed class WebUiWindow : Window {
    private readonly bool _smoke;
    private readonly WebView2Host _host;
    private string? _smokeNonce;

    /// <summary>构造 WebUI 窗口</summary>
    /// <param name="pageUri">页面 URI（file:// 离线加载）</param>
    /// <param name="userDataDir">WebView2 用户数据目录</param>
    /// <param name="smoke">冒烟模式：握手 + 桥接往返完成即退出</param>
    public WebUiWindow(string pageUri, string userDataDir, bool smoke) {
        _smoke = smoke;
        Title = "JoinCode";
        Width = 1440;
        Height = 900;
        ClientSize = new Size(1440, 900);
        _host = new WebView2Host(pageUri, userDataDir);
        _host.InitFailed += ShowFallback;
        _host.WebViewReady += OnWebViewReady;
        _host.MessageReceived += OnBridgeMessage;
        Content = _host;
    }

    /// <summary>桥接消息处理：web/ready 握手回 app/init（冒烟附带 ping）；冒烟校验 smoke/pong</summary>
    private void OnBridgeMessage(BridgeMessage message) {
        switch (message) {
            case WebReadyMessage:
                App.LogDiag("[WebUI] BRIDGE web/ready");
                _host.Send(new AppInitMessage(
                    GuiPalette.CurrentVariant.ToString(),
                    typeof(WebUiWindow).Assembly.GetName().Version?.ToString() ?? "dev"));
                if (_smoke) {
                    _smokeNonce = Guid.NewGuid().ToString("N");
                    _host.Send(new SmokePingMessage(_smokeNonce));
                }
                break;
            case SmokePongMessage pong when _smoke:
                var matched = pong.Nonce == _smokeNonce;
                App.LogDiag(matched
                    ? $"[WebUI] BRIDGE-SMOKE ok nonce={pong.Nonce}"
                    : $"[WebUI] BRIDGE-SMOKE mismatch got={pong.Nonce} want={_smokeNonce}");
                Environment.Exit(matched ? 0 : 4);
                break;
        }
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

    /// <summary>WebView2 就绪：写启动诊断标记；冒烟模式导航完成后启动桥接看门狗</summary>
    private void OnWebViewReady(CoreWebView2 core) {
        App.LogDiag($"[WebUI] WEBVIEW2-READY runtime={core.Environment.BrowserVersionString}");
        if (!_smoke) {
            return;
        }
        core.NavigationCompleted += (sender, e) => {
            App.LogDiag($"[WebUI] WEBUI-NAVIGATED success={e.IsSuccess}");
            if (!e.IsSuccess) {
                Environment.Exit(2);
                return;
            }
            _ = BridgeWatchdogAsync();
        };
    }

    /// <summary>冒烟看门狗：握手 + ping/pong 若 5s 内未完成（正常路径已 Exit）则判桥接超时</summary>
    private static async Task BridgeWatchdogAsync() {
        await Task.Delay(TimeSpan.FromSeconds(5));
        App.LogDiag("[WebUI] BRIDGE-SMOKE timeout");
        Environment.Exit(4);
    }
}
