namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH Web UI 窗口（ADR 0143）：起始 <see cref="DshWebHost"/>（回环 HTTP + WS），
/// 内容区由 WebView2 加载该宿主页面。原自研 WebUI（<c>--webui-smoke</c>）暂保留作对照。
/// </summary>
internal sealed class DshWebUiWindow : Window {
    private readonly DshHostServices _services;
    private readonly DshWebHost _host;
    private readonly WebView2Host _view;
    private readonly TranscriptStore _bootstrapTranscripts;
    private Hosting.IJccChatSession? _engine;

    /// <summary>构造并启动宿主</summary>
    /// <param name="userDataDir">WebView2 用户数据目录</param>
    internal DshWebUiWindow(string userDataDir) {
        Title = "JoinCode";
        Width = 1440;
        Height = 900;
        ClientSize = new Size(1440, 900);
        _services = new DshHostServices(new PhysicalFileSystem());
        // 侧栏会话列表是纯落盘读取：先挂轻量实例，窗口与历史会话同时可见，不必等引擎组装（28~32s）
        _bootstrapTranscripts = new TranscriptStore(new PhysicalFileSystem());
        _services.Sessions.AttachTranscripts(_bootstrapTranscripts);
        _host = DshWebHost.Start(_services);
        _view = new WebView2Host(_host.BaseUrl, userDataDir);
        _view.InitFailed += ShowFallback;
        Content = _view;
        DshDirectoryPicker.Register(PickFolderAsync);
        _ = _services.Settings.InitializeAsync(CancellationToken.None);
        // 权限预设写路径 → 引擎 SetPermissionModeAsync 立即生效（TASK058 B3）
        _services.Settings.PermissionPresetChanged += OnPermissionPresetChanged;
        _ = LoadModelCatalogAsync();
        _ = AttachEngineAsync();
        App.LogDiag($"[DSH-WEB] window created url={_host.BaseUrl}");
    }

    /// <summary>权限预设变更路由：settings/mutate 写 defaultPreset 成功后应用到引擎（未就绪只记日志，引擎 attach 时读存档补应用）</summary>
    private void OnPermissionPresetChanged(string preset) {
        if (PermissionModeExtensions.FromValue(preset) is null) {
            App.LogDiag($"[DSH-WEB] permission preset ignored unknown value: {preset}");
            return;
        }
        if (_engine is null) {
            App.LogDiag($"[DSH-WEB] permission preset deferred until engine ready: {preset}");
            return;
        }
        _ = ApplyPresetAsync(preset);
    }

    /// <summary>
    /// 应用权限预设（TASK066 C7）：统一走宿主服务——写引擎、更新会话投影、广播 plan/mode 事件
    /// 与 session/control 投影帧；应用失败只记日志，不回滚设置（下次 attach 会重读存档）。
    /// </summary>
    private async Task ApplyPresetAsync(string preset) {
        try {
            var (ok, message) = await _services.ApplyPermissionPresetAsync(preset, CancellationToken.None);
            App.LogDiag(ok
                ? $"[DSH-WEB] permission preset applied={preset}"
                : $"[DSH-WEB] permission preset apply rejected={preset}: {message}");
        } catch (Exception ex) {
            App.LogDiag($"[DSH-WEB] permission preset apply failed={preset}: {ex.Message}");
        }
    }

    /// <summary>
    /// 启动期读 settings.json 构建模型目录（引擎未就绪也能渲染模型选择），引擎 attach 后热切换成引擎真值。
    /// 读取失败只让目录为空，不影响前端渲染。
    /// </summary>
    private async Task LoadModelCatalogAsync() {
        try {
            await _services.Models.LoadAsync(new PhysicalFileSystem(), SettingsLoader.GetUserSettingsPath(), CancellationToken.None);
            App.LogDiag($"[DSH-WEB] model catalog from settings: {_services.Models.Current.Groups.Count} groups");
        } catch (Exception ex) {
            App.LogDiag($"[DSH-WEB] model catalog load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 后台组装自有引擎，就绪后把会话存储交给宿主（渐进式：窗口先显示，数据后到）。
    /// 引擎失败只影响数据，Web UI 仍可用（宿主方法返回空结果 + 引导错误）。引擎随窗口关闭释放。
    /// 权限确认链（TASK058 B1）：注入 PermissionConfirmationHandler — 引擎确认点经 DshApprovalBridge
    /// 发 $events waterfall 帧给前端审批面板，回帖结算后返回决策（前端未连接/断开一律 fail-closed Deny）。
    /// </summary>
    private async Task AttachEngineAsync() {
        try {
            var engine = await Hosting.JccChatSession.CreateAsync();
            _engine = engine;
            _services.AttachEngine(engine);
            engine.PermissionConfirmationHandler = request => _services.Approvals.RequestAsync(request);
            // 提问卡片（TASK066 C7）：引擎 ask_user_question → $events user-questions/request → 前端单卡多问
            engine.AskUserQuestionsCallback = (questions, cancellationToken) =>
                _services.Questions.RequestAsync(questions, cancellationToken);
            if (engine.TranscriptService is { } transcripts) {
                _services.Sessions.AttachTranscripts(transcripts);
                App.LogDiag("[DSH-WEB] engine attached: transcripts ready");
            } else {
                App.LogDiag("[DSH-WEB] engine ready but ITranscriptService unavailable");
            }
            _services.Models.AttachEngine(engine.CurrentVendor, engine.CurrentModelId, engine.VendorModelMap);
            await ApplyStoredPermissionPresetAsync(engine);
            App.LogDiag($"[DSH-WEB] engine model catalog: vendor={engine.CurrentVendor} model={engine.CurrentModelId} groups={engine.VendorModelMap.Count}");
        } catch (Exception ex) {
            App.LogDiag($"[DSH-WEB] engine attach failed: {ex.Message}");
        }
    }

    /// <summary>引擎就绪后补应用存档的权限预设（用户上次会话保存的 defaultPreset，TASK058 B3）</summary>
    private async Task ApplyStoredPermissionPresetAsync(Hosting.IJccChatSession engine) {
        try {
            var describe = _services.Settings.Describe();
            var view = describe.Namespaces.FirstOrDefault(ns => string.Equals(ns.Ns, "permission", StringComparison.Ordinal));
            if (view?.Value.TryGetProperty("defaultPreset", out var preset) is true
                && preset.ValueKind == JsonValueKind.String
                && preset.GetString() is { } presetValue
                && PermissionModeExtensions.FromValue(presetValue) is { }) {
                await ApplyPresetAsync(presetValue);
                App.LogDiag($"[DSH-WEB] stored permission preset applied: {presetValue}");
            }
        } catch (Exception ex) {
            App.LogDiag($"[DSH-WEB] stored permission preset apply failed: {ex.Message}");
        }
    }

    /// <summary>前端 directoryPicker/pick 的原生实现：UI 线程弹目录选择对话框，取消返回 null</summary>
    private async Task<string?> PickFolderAsync(CancellationToken cancellationToken) {
        var folders = await Dispatcher.UIThread.InvokeAsync(() => StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions {
            Title = "选择工作区目录",
            AllowMultiple = false,
        }));
        if (cancellationToken.IsCancellationRequested) {
            return null;
        }
        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    /// <summary>WebView2 初始化失败回退（不崩溃，给出可操作提示）</summary>
    private void ShowFallback(string reason) {
        App.LogDiag($"[DSH-WEB] init failed: {reason}");
        Content = new StackPanel {
            Margin = new Thickness(24),
            Spacing = 8,
            Children = {
                new TextBlock { Text = "WebView2 初始化失败，无法加载 DSH Web UI。", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "请安装 Microsoft Edge WebView2 Runtime 后重试。", TextWrapping = TextWrapping.Wrap },
            }
        };
    }

    /// <summary>窗口关闭：注销目录选择器 + 停止 HTTP/WS 宿主 + 释放引擎与轻量会话存储（WebView2 由控件自身关闭）</summary>
    protected override void OnClosed(EventArgs e) {
        DshDirectoryPicker.Register(null);
        _ = _host.DisposeAsync().AsTask().ContinueWith(task => {
            if (task.IsFaulted) {
                App.LogDiag($"[DSH-WEB] host dispose failed: {task.Exception?.Message}");
            }
        }, TaskScheduler.Default);
        _ = _bootstrapTranscripts.DisposeAsync().AsTask().ContinueWith(task => {
            if (task.IsFaulted) {
                App.LogDiag($"[DSH-WEB] bootstrap transcript dispose failed: {task.Exception?.Message}");
            }
        }, TaskScheduler.Default);
        if (_engine is { } engine) {
            _engine = null;
            _ = engine.DisposeAsync().AsTask().ContinueWith(task => {
                if (task.IsFaulted) {
                    App.LogDiag($"[DSH-WEB] engine dispose failed: {task.Exception?.Message}");
                }
            }, TaskScheduler.Default);
        }
        base.OnClosed(e);
    }
}
