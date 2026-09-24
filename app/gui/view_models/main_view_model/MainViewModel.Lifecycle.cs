namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel partial — 生命周期管理（构造/引擎热切换/Mock回退/释放）与权限回调。
/// 从 MainViewModel.cs 拆出以控制文件行数（关注点分离）。
/// </summary>
public sealed partial class MainViewModel {
    private bool _disposed;

    /// <summary>初始化 MainViewModel 实例</summary>
    public MainViewModel(IJccChatSession? session = null, Persistence.GuiSessionStore? store = null, Persistence.GuiPreferencesStore? preferencesStore = null, IModelConfigLoader? modelConfigLoader = null) {
        _modelConfigLoader = modelConfigLoader ?? new ModelConfigLoader();
        _realSession = session;
        // 配置服务的文件系统跟随 preferencesStore：生产传 null → PhysicalFileSystem；
        // 测试传 InMemory store → 全程密闭，不读写真实 ~/.jcc/settings.json（防并行测试互扰+污染用户配置）
        _fileSystem = preferencesStore?.FileSystem ?? new IO.FileSystem.PhysicalFileSystem();
        _configService = new Core.Configuration.ConfigurationService(_fileSystem);
        _session = session ?? new Hosting.PlaceholderChatSession(_configService, _modelConfigLoader);
        _sessionStore = store ?? new Persistence.GuiSessionStore(new IO.FileSystem.PhysicalFileSystem());
        _preferencesStore = preferencesStore ?? new Persistence.GuiPreferencesStore(new IO.FileSystem.PhysicalFileSystem());
        _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;
        _session.AskUserQuestionDialogCallback = AskUserQuestionCallback;
        // T9：斜杠命令确认/退出 — handler 由 View 注入（弹确认框），退出事件转发给 View 关窗
        _session.SlashConfirmHandler = message => SlashConfirmHandler?.Invoke(message) ?? false;
        _session.ExitRequested += () => ExitRequested?.Invoke();

        // 后台代理管理面板 — 数据源绑定会话门面，快照计数同步到全局状态条
        BackgroundPanel = new BackgroundAgentsPanelViewModel(
            fetcher: ct => _session.GetBackgroundAgentsAsync(ct),
            stopper: (id, ct) => _session.StopBackgroundAgentAsync(id, ct));
        BackgroundPanel.SnapshotApplied += count => RunStatus.SetBackgroundCount(count);

        _selectedEffort = _session.EffortLevel.ToValue();
        Messages.CollectionChanged += OnMessagesChanged;
        _ = LoadPersistedSessionsAsync();
        NewConversation();

        // 注册持久化路由（在 LoadPreferences 之前，加载期门控关闭不触发）
        RegisterPersistActions();

        // 加载 GUI 偏好并应用到 UI 属性（启动时恢复上次显示的内容）
        _ = LoadPreferencesAsync();

        if (session is not null) {
            RebuildConnectionOptions();
            RefreshModelOptions();
            _selectedModel = _session.CurrentModelId;
            _selectedModelOption = GetModelById(_session.CurrentModelId);
            using (var _ = _gate.EnterRefreshingConfigScope()) {
                SelectedConnection = GetConnectionById(session.CurrentVendor)
                    ?? _connectionDropdown.ConnectionOptions.FirstOrDefault();
            }
            IsEngineLoaded = true;
            StartModelConfigWatch();
            // 引擎就绪后把偏好里的采样参数应用到引擎
            ApplyPreferencesToEngine();
            // 订阅 settings.json theme 变更 + 从 settings.json 读主题（唯一数据源，对齐 CLI /theme）
            session.ThemeChanged += OnThemeChanged;
            LoadThemeFromSettings();
        } else {
            StatusText = "正在加载引擎…";
            RebuildConnectionOptions();
            ViewModelDiagnosticsLogger.WriteDebug($"Constructor else: currentVendor={_session.CurrentVendor} connectionCount={_connectionDropdown.ConnectionOptions.Count} ids=[{string.Join(",", _connectionDropdown.ConnectionOptions.Select(c => c.Id))}]");
            using (var _ = _gate.EnterRefreshingConfigScope()) {
                SelectedConnection = GetConnectionById(_session.CurrentVendor)
                    ?? _connectionDropdown.ConnectionOptions.FirstOrDefault();
            }
            ViewModelDiagnosticsLogger.WriteDebug($"Constructor else: SelectedConnection={SelectedConnection?.Id}");
            RefreshModelOptions();
            _selectedModelOption = GetModelById(_session.CurrentModelId)
                ?? ModelOptions.FirstOrDefault();
            _selectedModel = _selectedModelOption?.Id ?? _session.CurrentModelId;
            // 引擎未就绪时仍从 settings.json 读主题（PlaceholderChatSession 持有 _configService 可读）
            LoadThemeFromSettings();
        }
    }

    /// <summary>获取可用斜杠命令清单 — 委托到引擎 session，由源码生成器自动提取</summary>
    public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands()
        => _session.GetAvailableSlashCommands();

    /// <summary>
    /// 后台引擎组装完成后热切换 — 将占位会话替换为真实引擎会话并刷新全部派生状态。
    /// 由 App 在后台线程完成 <see cref="JccChatSession.CreateAsync"/> 后调用（UI 线程）。
    /// </summary>
    public void AttachRealSession(IJccChatSession session) {
        ViewModelDiagnosticsLogger.WriteDebug($"AttachRealSession: currentVendor={session.CurrentVendor} currentModel={session.CurrentModelId}");
        _realSession = session;
        _session = session;
        _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;
        _session.AskUserQuestionDialogCallback = AskUserQuestionCallback;

        RebuildConnectionOptions();
        RefreshModelOptions();
        SelectedModel = _session.CurrentModelId;
        SelectedModelOption = GetModelById(_session.CurrentModelId);
        SelectedEffort = _session.EffortLevel.ToValue();
        using (var _ = _gate.EnterRefreshingConfigScope()) {
            SelectedConnection = GetConnectionById(session.CurrentVendor)
                ?? _connectionDropdown.ConnectionOptions.FirstOrDefault();
        }
        ViewModelDiagnosticsLogger.WriteDebug($"AttachRealSession: SelectedConnection={SelectedConnection?.Id}");

        // 清空延迟构建的斜杠命令缓存，改用真实引擎的命令清单
        _slashCommandCache = [];
        RefreshSlashSuggestions();

        OnPropertyChanged(nameof(IsMockConnection));
        IsEngineLoaded = true;
        // 需求11：异步填充子会话树（快照避免跨线程）
        _ = Task.Run(() => PopulateSubSessionsAsync(Sessions.ToArray()));
        _ = Task.Run(async () => {
            try {
                var tools = await session.GetAvailableToolsAsync().WaitAsync(Timeout);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _availableToolsCache = tools);
            } catch (Exception ex) {
                ViewModelDiagnosticsLogger.WriteError(ex);
            }
        });
        _ = Task.Run(async () => {
            try {
                var agents = await session.GetAvailableSubAgentsAsync().WaitAsync(Timeout);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _availableSubAgentsCache = agents);
            } catch (Exception ex) {
                ViewModelDiagnosticsLogger.WriteError(ex);
            }
        });
        StatusText = $"已连接真实引擎 {session.CurrentVendor}";
        StartModelConfigWatch();
        // 引擎热切换后把偏好里的采样参数应用到引擎
        ApplyPreferencesToEngine();
        // 订阅 settings.json theme 变更（外部 CLI /theme 驱动 GUI 热重载）+ 从 settings.json 读主题
        session.ThemeChanged += OnThemeChanged;
        LoadThemeFromSettings();

        // 引擎就绪后注入 ITranscriptService,切换到统一入口(.json + 子目录,与 CLI --continue 共享)
        // 构造时 LoadPersistedSessions 用旧 .json 兜底,此处切换后重新加载刷新侧边栏
        if (session.TranscriptService is not null) {
            _sessionStore.SetTranscriptService(session.TranscriptService);
            Sessions.Clear();
            _ = LoadPersistedSessionsAsync();
        }
    }

    /// <summary>引擎加载失败时回退到 Mock 引擎（IsMockConnection 驱动按钮状态，供应商下拉保持真实列表）</summary>
    public void FallbackToMock() {
        _session = _mockSession ??= new Hosting.PlaceholderChatSession(_configService);
        _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;
        _session.AskUserQuestionDialogCallback = AskUserQuestionCallback;
        RebuildConnectionOptions();
        SelectedConnection = _connectionDropdown.ConnectionOptions.FirstOrDefault();
        RefreshModelOptions();
        SelectedModel = _session.CurrentModelId;
        SelectedModelOption = GetModelById(_session.CurrentModelId);
        IsEngineLoaded = true;
        // 引擎失败回退后仍从 settings.json 读主题（PlaceholderChatSession 可读写 settings.json）
        LoadThemeFromSettings();
    }

    /// <summary>
    /// 权限确认回调 — 由 View 层注入（弹窗实现），引擎权限待确认时调用。
    /// 未注入时默认拒绝（等价于 Deny），保证无弹窗环境下引擎行为可预期。
    /// </summary>
    public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmCallback { get; set; }

    /// <summary>
    /// AskUserQuestion 弹窗回调 — 由 View 层注入（弹窗实现），AskUserQuestion 工具调用时触发。
    /// 未注入时回退到自动选择第一项。
    /// </summary>
    public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionCallback { get; set; }

    /// <summary>网关权限确认请求 → 委托给 View 层弹窗回调；未注入回调时默认拒绝</summary>
    private Task<PermissionConfirmationDecision> OnPermissionConfirmationRequestedAsync(PermissionConfirmationRequest request)
        => PermissionConfirmCallback is not null
            ? PermissionConfirmCallback(request)
            : Task.FromResult(PermissionConfirmationDecision.Deny);

    /// <summary>切换 Mock 引擎模式 — 独立按钮命令，按下进入 Mock 演示，再按切回真实引擎</summary>
    [RelayCommand]
    private void ToggleMock() {
        if (_session is PlaceholderChatSession && _realSession is not null) {
            _session = _realSession;
            StatusText = $"已切回真实引擎 {_session.CurrentVendor}";
        } else {
            _session = _mockSession ??= new Hosting.PlaceholderChatSession(_configService);
            _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;
            _session.AskUserQuestionDialogCallback = AskUserQuestionCallback;
            StatusText = $"已切换到 Mock 引擎（演示），模型 {_session.CurrentModelId}";
        }
        RefreshModelOptions();
        OnPropertyChanged(nameof(IsMockConnection));
        SelectedModelOption = GetModelById(_session.CurrentModelId)
            ?? ModelOptions.FirstOrDefault();
        SelectedModel = SelectedModelOption?.Id;
    }

    /// <summary>
    /// 释放引擎资源 — 窗口关闭时由 MainWindow.OnWindowClosed 调用。
    /// 避免 HTTP 连接池/FileSystemWatcher/后台任务泄漏导致进程不退（孤儿进程 + 文件锁）。
    /// 异步等待真实/模拟会话释放完成，避免 fire-and-forget 丢失异常与释放顺序竞态。
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;
        if (_modelConfigWatcher is not null) await _modelConfigWatcher.DisposeAsync();
        _modelConfigWatcher = null;
        _sendCts?.Cancel();
        _sendCts?.Dispose();
        _sendCts = null;
        if (_realSession is not null)
            await _realSession.DisposeAsync();
        if (_mockSession is not null)
            await _mockSession.DisposeAsync();
    }
}