namespace JoinCode.Gui.WebView;

/// <summary>
/// WebUI 宿主窗口 — 内容区由 WebView2 承载自研 Web UI（B2 路线，ADR 0138）。
/// 由 <c>--webui</c> 启动参数启用；<c>--webui-smoke</c> 为 E2E 冒烟：
/// 导航成功 → 等 web/ready 握手 → smoke/ping·smoke/pong 往返 + sessions/list·web/sessions-rendered 往返（5s 看门狗）。
/// 退出码：0 全通 / 2 导航失败 / 3 初始化失败 / 4 桥接超时或 nonce 不符（ADR 0141 决策 5）。
/// 初始化失败时窗口内展示回退提示（不崩溃），并写入启动诊断日志。
/// </summary>
internal sealed class WebUiWindow : Window {
    private readonly bool _smoke;
    private readonly WebView2Host _host;
    private readonly Persistence.GuiSessionStore _sessionStore = new(new IO.FileSystem.PhysicalFileSystem());
    private readonly CancellationTokenSource _lifetimeCts = new();
    private IJccChatSession? _session;
    private WebChatFlow? _flow;
    private string? _smokeNonce;
    private bool _smokePongOk;
    private bool _smokeRendered;

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
        if (!_smoke) {
            // 冒烟只验桥接链路（确定性），不加载引擎；产品模式后台组装真实引擎（对齐 App.CreateViewModel 模式）
            StartEngine();
        }
    }

    /// <summary>后台组装真实引擎，完成后 UI 线程热切换（失败回 app/error，不崩溃）</summary>
    private void StartEngine() {
        _ = Task.Run(async () => {
            try {
                var session = await JccChatSession.CreateAsync();
                Dispatcher.UIThread.Post(() => AttachEngine(session));
            } catch (Exception ex) {
                App.LogDiag($"[WebUI] engine load failed: {ex.Message}");
                Dispatcher.UIThread.Post(() => _host.Send(new AppErrorMessage(
                    $"引擎加载失败：{ex.Message}", "查看 dumps 崩溃日志定位后重启应用")));
            }
        });
    }

    /// <summary>挂接真实引擎：建会话流投影层 + 权限 fail-closed 兜底 + 会话存储切换到统一入口后刷新列表</summary>
    private void AttachEngine(IJccChatSession session) {
        _session = session;
        _flow = new WebChatFlow(session);
        session.PermissionConfirmationHandler = DenyPermissionAsync;
        if (session.TranscriptService is { } transcript) {
            _sessionStore.SetTranscriptService(transcript);
            _ = SendSessionsListAsync(_lifetimeCts.Token);
        }
        App.LogDiag($"[WebUI] ENGINE attached vendor={session.CurrentVendor} model={session.CurrentModelId}");
    }

    /// <summary>权限确认 fail-closed 兜底（P6-5 对话框接入前一律拒绝，安全方向）</summary>
    private static Task<PermissionConfirmationDecision> DenyPermissionAsync(PermissionConfirmationRequest request) {
        App.LogDiag($"[WebUI] permission denied (P6-5 前 fail-closed) tool={request.ToolName}");
        return Task.FromResult(PermissionConfirmationDecision.Deny);
    }

    /// <summary>桥接消息处理：web/ready 握手回 app/init + 下发会话列表（冒烟附带 ping）；冒烟校验 pong + 列表渲染 ACK</summary>
    private void OnBridgeMessage(BridgeMessage message) {
        switch (message) {
            case WebReadyMessage:
                App.LogDiag("[WebUI] BRIDGE web/ready");
                _host.Send(new AppInitMessage(
                    GuiPalette.CurrentVariant.ToString(),
                    typeof(WebUiWindow).Assembly.GetName().Version?.ToString() ?? "dev"));
                _ = SendSessionsListAsync(_lifetimeCts.Token);
                if (_smoke) {
                    _smokeNonce = Guid.NewGuid().ToString("N");
                    _host.Send(new SmokePingMessage(_smokeNonce));
                }
                break;
            case SessionSelectMessage select:
                App.LogDiag($"[WebUI] BRIDGE session/select id={select.Id}");
                _ = OpenSessionAsync(select.Id, _lifetimeCts.Token);
                break;
            case ChatSendMessage send:
                App.LogDiag($"[WebUI] BRIDGE chat/send len={send.Prompt.Length}");
                if (_flow is null) {
                    _host.Send(new AppErrorMessage("引擎尚未就绪", "等待启动完成后重试；若长时间未就绪查看 dumps/startup_timing.log"));
                    break;
                }
                _ = _flow.SendAsync(send.Prompt, _host.Send, _lifetimeCts.Token);
                break;
            case SmokePongMessage pong when _smoke:
                if (pong.Nonce != _smokeNonce) {
                    App.LogDiag($"[WebUI] BRIDGE-SMOKE mismatch got={pong.Nonce} want={_smokeNonce}");
                    Environment.Exit(4);
                    break;
                }
                _smokePongOk = true;
                TryCompleteSmoke();
                break;
            case WebSessionsRenderedMessage rendered when _smoke:
                App.LogDiag($"[WebUI] BRIDGE-SMOKE sessions-rendered count={rendered.Count}");
                _smokeRendered = true;
                TryCompleteSmoke();
                break;
        }
    }

    /// <summary>读取真实会话列表下发 web（sessions/list）；失败回 app/error 指引（fail-closed，不崩溃）</summary>
    private async Task SendSessionsListAsync(CancellationToken cancellationToken) {
        try {
            var summaries = await _sessionStore.ListSessionsAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var sessions = summaries
                .Select(s => new SessionSummaryDto(s.Id, string.IsNullOrWhiteSpace(s.Title) ? s.Id : s.Title))
                .ToList();
            _host.Send(new SessionsListMessage(sessions));
            App.LogDiag($"[WebUI] BRIDGE sessions/list count={sessions.Count}");
        } catch (OperationCanceledException) {
            // 窗口关闭中：静默放弃（Dispose 后不得再触碰宿主）
        } catch (Exception ex) {
            App.LogDiag($"[WebUI] sessions/list failed: {ex.Message}");
            _host.Send(new AppErrorMessage("会话列表读取失败", "检查 sessions 目录权限后重开窗口"));
        }
    }

    /// <summary>打开会话：持久化历史下发 web（chat/history）+ 切换引擎会话并灌入上下文（对齐 MainViewModel.SelectSession 语义）</summary>
    private async Task OpenSessionAsync(string sessionId, CancellationToken cancellationToken) {
        try {
            var data = await _sessionStore.LoadAsync(sessionId);
            cancellationToken.ThrowIfCancellationRequested();
            var entries = (data?.Messages ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => new ChatHistoryEntry(m.Role, m.Content))
                .ToList();
            _host.Send(new ChatHistoryMessage(entries));
            if (_session is { } session) {
                session.SwitchSession(sessionId);
                var history = entries
                    .Select(e => (MessageRoleExtensions.FromValue(e.Role) ?? MessageRole.User, e.Content))
                    .ToList();
                await session.LoadHistoryAsync(history, cancellationToken);
            }
            App.LogDiag($"[WebUI] session/select loaded id={sessionId} count={entries.Count}");
        } catch (OperationCanceledException) {
            // 窗口关闭中：静默放弃
        } catch (Exception ex) {
            App.LogDiag($"[WebUI] session load failed id={sessionId}: {ex.Message}");
            _host.Send(new AppErrorMessage("会话加载失败", "检查 sessions 目录后重试或选择其它会话"));
        }
    }

    /// <summary>窗口关闭：取消在途的流式发送/会话加载（释放后异步操作不得继续访问宿主）</summary>
    protected override void OnClosed(EventArgs e) {
        _lifetimeCts.CancelAndDisposeSafe();
        base.OnClosed(e);
    }

    /// <summary>冒烟完成判定：pong nonce 匹配 + 会话列表渲染 ACK 均到达后退出码 0</summary>
    private void TryCompleteSmoke() {
        if (!_smokePongOk || !_smokeRendered) {
            return;
        }
        App.LogDiag($"[WebUI] BRIDGE-SMOKE ok nonce={_smokeNonce}");
        Environment.Exit(0);
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
