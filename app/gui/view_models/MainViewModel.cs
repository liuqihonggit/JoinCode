namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 主窗口 ViewModel — 承载引擎会话门面与基础对话占位。
/// 依赖注入仅走 <see cref="IJccChatSession"/>，不触碰引擎内部实现。
/// </summary>
public sealed partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private IJccChatSession? _realSession;
    private IJccChatSession? _mockSession;
    private IJccChatSession _session;
    private readonly Persistence.GuiSessionStore _sessionStore;
    private readonly Persistence.GuiPreferencesStore _preferencesStore;
    private readonly IModelConfigLoader _modelConfigLoader;
    /// <summary>独立配置服务 — 引擎加载失败时仍可持久化 settings.json（主题/供应商/模型/推理力度）</summary>
    private readonly IConfigurationService _configService;
    /// <summary>连接/模型下拉管理器 — 管理供应商连接列表和模型下拉选项</summary>
    private readonly ConnectionDropdownManager _connectionDropdown = new();
    private bool _isRefreshingConfig;
    private bool _isApplyingExternalTheme;
    private IFileSystemWatcher? _modelConfigWatcher;
    private readonly IFileSystem _fileSystem;

    /// <summary>异步操作硬超时（防止命令续体在单线程上下文死锁）</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "未连接";

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string? _selectedModel;

    /// <summary>采样温度（设置面板滑块）</summary>
    [ObservableProperty]
    private double _temperature = 0.7;

    /// <summary>最大输出 token（设置面板滑块）</summary>
    [ObservableProperty]
    private int _maxTokens = 4096;

    /// <summary>是否流式输出（设置面板开关）</summary>
    [ObservableProperty]
    private bool _streamingEnabled = true;

    /// <summary>F3：Enter 直接发送（false → Ctrl+Enter 发送、Enter 换行）</summary>
    [ObservableProperty]
    private bool _enterSends = false;

    /// <summary>F2：双击 ESC 终止手势开关</summary>
    [ObservableProperty]
    private bool _doubleEscStop = true;

    /// <summary>无人值守模式 — AI 持续推进长任务，红灯自动执行+审计，黑灯仍拒绝 — ADR 0012</summary>
    [ObservableProperty]
    private bool _isUnattendedMode = false;

    /// <summary>防丢字符二次确认 — 防止 MTP 加速推理时丢字符/乱入字符导致命令变形 — ADR 0012</summary>
    [ObservableProperty]
    private bool _isAntiCharLossConfirm = false;

    /// <summary>窗口震动通知开关 — 子代理调用 shake_window 时是否震动窗口 — ADR 0109</summary>
    [ObservableProperty]
    private bool _windowShakeEnabled = true;

    /// <summary>聊天室模式开关 — 是否启用跨进程子代理聊天室广播 — ADR 0109</summary>
    [ObservableProperty]
    private bool _chatRoomEnabled = true;

    /// <summary>输入栏占位提示 — 随发送键位偏好联动</summary>
    public string SendHintText => EnterSends
        ? "输入消息，Enter 发送 / Shift+Enter 换行…"
        : "输入消息，Ctrl+Enter 发送 / Enter 换行…";

    partial void OnEnterSendsChanged(bool value) => OnPropertyChanged(nameof(SendHintText));

    /// <summary>快捷键面板项列表（需求3）— 从 GuiPreferences 加载，录制后写回持久化</summary>
    public ObservableCollection<HotkeyItemVm> HotkeyItems { get; } = [];

    /// <summary>切换快捷键录制状态：同一时间只允许一个项录制中</summary>
    [RelayCommand]
    private void ToggleHotkeyRecording(HotkeyItemVm? item)
    {
        if (item is null)
            return;
        foreach (var h in HotkeyItems)
            h.IsRecording = h == item && !h.IsRecording;
    }

    /// <summary>恢复单个快捷键为默认值</summary>
    [RelayCommand]
    private void ResetHotkey(HotkeyItemVm? item)
    {
        if (item is null)
            return;
        item.Gesture = HotkeyDefaults.Get(item.ActionKey);
        SaveHotkeysToPreferences();
    }

    /// <summary>录制完成后由 View 层调用：设置键位并持久化</summary>
    public void ApplyRecordedHotkey(HotkeyItemVm item, string gesture)
    {
        item.Gesture = gesture;
        item.IsRecording = false;
        SaveHotkeysToPreferences();
    }

    /// <summary>从 HotkeyItems 获取指定动作的当前键位</summary>
    private string GetHotkeyGesture(string actionKey)
    {
        foreach (var h in HotkeyItems)
            if (h.ActionKey == actionKey)
                return h.Gesture;
        return HotkeyDefaults.Get(actionKey);
    }

    /// <summary>从 HotkeyItems 写回 GuiPreferences 并持久化</summary>
    private void SaveHotkeysToPreferences()
    {
        if (!_isPreferencesLoaded)
            return;
        try
        {
            var existing = _preferencesStore.Load();
            foreach (var h in HotkeyItems)
            {
                switch (h.ActionKey)
                {
                    case "Send": existing.HotkeySend = h.Gesture; break;
                    case "Newline": existing.HotkeyNewline = h.Gesture; break;
                    case "Stop": existing.HotkeyStop = h.Gesture; break;
                    case "NewSession": existing.HotkeyNewSession = h.Gesture; break;
                    case "ClearHistory": existing.HotkeyClearHistory = h.Gesture; break;
                    case "ToggleSettings": existing.HotkeyToggleSettings = h.Gesture; break;
                }
            }
            _preferencesStore.Save(existing);
        }
        catch (Exception ex)
        {
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }

    /// <summary>推理力度选项（对齐 CLI /effort：low/medium/high/max/auto）</summary>
    public IReadOnlyList<string> EffortOptions { get; } =
        [EffortLevel.Low.ToValue(), EffortLevel.Medium.ToValue(), EffortLevel.High.ToValue(), EffortLevel.Max.ToValue(), EffortLevel.Auto.ToValue()];

    /// <summary>当前推理力度（ComboBox 显示值；变更时持久化对齐 CLI /effort）</summary>
    [ObservableProperty]
    private string _selectedEffort;

    partial void OnSelectedEffortChanged(string value)
    {
        var effort = EffortLevelHelper.ParseEffortLevel(value) ?? EffortLevel.Auto;
        if (_session.EffortLevel == effort)
            return;

        StatusText = $"推理力度: {value}";
        PersistSync(() => _session.SetEffortLevelAsync(effort));
    }

    /// <summary>设置面板是否展开</summary>
    [ObservableProperty]
    private bool _isSettingsPanelOpen;

    /// <summary>回底按钮是否可见（上滑浏览时显示，贴底时隐藏）</summary>
    [ObservableProperty]
    private bool _isBackToBottomVisible;

    /// <summary>引擎是否已加载完成（驱动连接/模型下拉框显隐，避免热切换闪烁）</summary>
    [ObservableProperty]
    private bool _isEngineLoaded;

    /// <summary>系统提示词（占位阶段仅编辑，P1 传入引擎）</summary>
    [ObservableProperty]
    private string _systemPrompt = "你是 JoinCode 助手，请用简洁清晰的中文回答。";

    /// <summary>消息区字号（设置面板滑块调节）</summary>
    [ObservableProperty]
    private double _fontSize = 14;

    /// <summary>状态三态类别（就绪/思考/错误，驱动顶栏状态指示器配色）</summary>
    public StatusKind StatusKind => StatusText switch
    {
        var s when s.StartsWith("错误", StringComparison.Ordinal) => StatusKind.Error,
        var s when s is "思考中…" or "已停止生成" or "已停止" => StatusKind.Busy,
        _ => StatusKind.Ready
    };

    partial void OnStatusTextChanged(string value)
        => OnPropertyChanged(nameof(StatusKind));

    /// <summary>当前字符数（随输入变化，驱动计数显示）</summary>
    [ObservableProperty]
    private int _charsCount;

    /// <summary>错误 toast 文案（非空时显示错误弹出提示）</summary>
    [ObservableProperty]
    private string? _errorToastText;

    /// <summary>是否显示错误 toast</summary>
    public bool HasErrorToast => ErrorToastText is not null;

    partial void OnErrorToastTextChanged(string? value)
        => OnPropertyChanged(nameof(HasErrorToast));

    /// <summary>复制错误 toast 时待写入剪贴板的文本（View 层消费后清空）</summary>
    [ObservableProperty]
    private string? _errorToastCopy;

    /// <summary>复制错误内容到剪贴板并关闭 toast</summary>
    [RelayCommand]
    private void CopyErrorToast()
    {
        if (string.IsNullOrEmpty(ErrorToastText))
            return;
        ErrorToastCopy = ErrorToastText;
        CopiedMessage = ErrorToastText.GetHashCode();
        ErrorToastText = null;
    }

    /// <summary>手动关闭错误 toast</summary>
    [RelayCommand]
    private void DismissErrorToast() => ErrorToastText = null;

    /// <summary>View 层消费完剪贴板文本后调用，清空待复制状态</summary>
    public void ClearErrorToastCopy() => ErrorToastCopy = null;

    /// <summary>空状态建议提问（点击填充输入框）</summary>
    public IReadOnlyList<string> SuggestedPrompts { get; } =
    [
        "帮我写一个 C# 斐波那契函数",
        "解释什么是中间件管道",
        "给一段代码 review 的检查清单"
    ];

    /// <summary>填充建议提问到输入框（不直接发送）</summary>
    [RelayCommand]
    private void UseSuggestion(string? prompt)
    {
        if (!string.IsNullOrWhiteSpace(prompt))
            InputText = prompt;
    }

    /// <summary>切换思考消息的折叠/展开状态（点击思考气泡标题触发）</summary>
    [RelayCommand]
    private void ToggleThinking(object? parameter)
    {
        if (parameter is ChatUiMessage msg && msg.IsThinking)
            msg.IsThinkingExpanded = !msg.IsThinkingExpanded;
    }

    /// <summary>切换系统提示词注入卡片的折叠/展开状态（需求10）</summary>
    [RelayCommand]
    private void TogglePrompt(object? parameter)
    {
        if (parameter is ChatUiMessage msg && msg.IsSystemPromptInjection)
            msg.IsPromptExpanded = !msg.IsPromptExpanded;
    }

    /// <summary>已发送消息历史（↑/↓ 回看）</summary>
    private readonly List<string> _inputHistory = [];

    /// <summary>历史回看游标（-1 表示未在回看中）</summary>
    private int _historyIndex = -1;

    /// <summary>是否正处于程序化填充输入（避免手动输入重置游标）</summary>
    private bool _isNavigating;

    /// <summary>全局运行状态条（spinner 动词/耗时/token 聚合/后台计数/卡死检测）</summary>
    public GlobalRunStatusViewModel RunStatus { get; } = new();

    /// <summary>
    /// 后台代理管理面板 — 数据源绑定会话门面（引擎运行列表+活跃 fork），
    /// 快照应用时同步 RunStatus 后台计数
    /// </summary>
    public BackgroundAgentsPanelViewModel BackgroundPanel { get; }

    /// <summary>输入字符数上限（超过即警示）</summary>
    public int MaxInputChars => MaxTokens * 3;

    /// <summary>输入是否超过建议上限（驱动顶栏警示与计数标红）</summary>
    public bool IsInputTooLong => CharsCount > MaxInputChars;

    /// <summary>从 settings.json 异步加载主题并应用到 IsDarkTheme（启动 / 引擎热切换后调用）</summary>
    private void LoadThemeFromSettings()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var theme = await _session.GetThemeAsync().WaitAsync(Timeout);
                // Auto 保持默认 IsDarkTheme（GUI 无 Auto 选项，避免按时间覆盖用户上次明确选择）
                if (theme is ThemeKind.Auto)
                    return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _isApplyingExternalTheme = true;
                    IsDarkTheme = ThemeConverter.ToIsDark(theme);
                    _isApplyingExternalTheme = false;
                });
            }
            catch (Exception ex)
            {
                ViewModelDiagnosticsLogger.WriteError(ex);
            }
        });
    }

    /// <summary>settings.json theme 外部变更事件处理 — 驱动 GUI 热重载（双向绑定）</summary>
    private void OnThemeChanged(object? sender, ThemeKind theme)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _isApplyingExternalTheme = true;
            IsDarkTheme = ThemeConverter.ToIsDark(theme);
            _isApplyingExternalTheme = false;
        });
    }

    /// <summary>输入框变化时同步字符计数并退出历史回看游标（斜杠刷新由 View 层防抖触发）</summary>
    partial void OnInputTextChanged(string value)
    {
        CharsCount = value.Length;
        OnPropertyChanged(nameof(IsInputTooLong));
        if (!_isNavigating)
            _historyIndex = -1;
    }

    /// <summary>切换深浅主题（占位阶段仅记录状态，UI 由 View 层响应）</summary>
    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    /// <summary>↑/↓ 翻看输入历史（-1 上一条，1 下一条；到底/顶时忽略）</summary>
    [RelayCommand]
    private void NavigateHistory(int direction)
    {
        if (_inputHistory.Count == 0)
            return;
        if (_historyIndex == -1 && direction > 0)
            return;
        var next = _historyIndex == -1
            ? _inputHistory.Count - 1
            : _historyIndex + direction;
        if (next < 0 || next >= _inputHistory.Count)
            return;
        _historyIndex = next;
        _isNavigating = true;
        InputText = _inputHistory[next];
        _isNavigating = false;
    }

    /// <summary>展开/收拢右侧设置面板</summary>
    [RelayCommand]
    private void ToggleSettingsPanel() => IsSettingsPanelOpen = !IsSettingsPanelOpen;

    /// <summary>恢复设置面板默认值（温度/最大长度/流式/系统提示词）</summary>
    [RelayCommand]
    private void ResetSettings()
    {
        Temperature = 0.7;
        MaxTokens = 4096;
        StreamingEnabled = true;
        SystemPrompt = "你是 JoinCode 助手，请用简洁清晰的中文回答。";
        FontSize = 14;
        SelectedEffort = EffortLevel.Auto.ToValue();
        StatusText = "已恢复默认设置";
    }

    /// <summary>在输入框插入分隔线（快速排版）</summary>
    [RelayCommand]
    private void InsertDivider() => ConcatInput("---\n");

    /// <summary>在输入框插入当前时间戳</summary>
    [RelayCommand]
    private void InsertTimestamp() => ConcatInput($"[{DateTime.Now:HH:mm:ss}] ");

    /// <summary>
    /// 请求打开子代理回放窗口 — View 层订阅 <see cref="TranscriptRequested"/>
    /// 弹出 TranscriptWindow（VM 不持有 Window 引用，保持可测性）
    /// </summary>
    public event Action<SubAgentRun>? TranscriptRequested;

    /// <summary>回放请求（agent 卡片"回放"按钮触发）</summary>
    [RelayCommand]
    private void OpenAgentTranscript(AgentRunVm? runVm)
    {
        if (runVm is null)
            return;
        TranscriptRequested?.Invoke(runVm.Run);
    }

    /// <summary>向输入框追加文本（光标定位到内容尾部）</summary>
    private void ConcatInput(string text) => InputText += text;

    /// <summary>请求滚动到底部（由 View 订阅执行实际 ScrollToLine UI 操作）</summary>
    public event Action? ScrollToBottomRequested;

    /// <summary>
    /// T9：退出请求 — /exit 确认通过后触发，MainWindow 订阅并 Close()。
    /// </summary>
    public event Action? ExitRequested;

    /// <summary>
    /// T9：斜杠命令确认框回调 — 由 MainWindow 注入（弹确认窗）。
    /// 未注入时默认拒绝（/exit /commit 等确认类命令取消执行）。
    /// </summary>
    public Func<string, bool>? SlashConfirmHandler { get; set; }

    /// <summary>跳到最新消息（回底按钮命令）</summary>
    [RelayCommand]
    private void ScrollToBottom()
    {
        IsBackToBottomVisible = false;
        ScrollToBottomRequested?.Invoke();
    }

}
