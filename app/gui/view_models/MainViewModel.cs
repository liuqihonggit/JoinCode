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

    /// <summary>刚复制的消息实例号（非 null 即显示"已复制"提示）</summary>
    [ObservableProperty]
    private int? _copiedMessage;

    /// <summary>是否刚复制了一条消息（驱动"已复制" toast 显隐）</summary>
    public bool HasCopied => CopiedMessage is not null;

    partial void OnCopiedMessageChanged(int? value)
        => OnPropertyChanged(nameof(HasCopied));

    /// <summary>清空复制反馈状态（toast 自动隐藏用）</summary>
    public void ClearCopiedState() => CopiedMessage = null;

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

    /// <summary>侧边栏会话列表（占位阶段）</summary>
    public ObservableCollection<SessionItem> Sessions { get; } = [];

    /// <summary>消息搜索关键词（非空时仅显示匹配消息）</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>是否处于搜索状态（驱动搜索框样式与计数显示）</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>已过滤消息集合（按 SearchText 关键词；空搜索返回全部）</summary>
    public IEnumerable<ChatUiMessage> FilteredMessages => IsSearching
        ? Messages.Where(m => (m.Content ?? string.Empty).Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || (m.ToolResultText ?? string.Empty).Contains(SearchText, StringComparison.OrdinalIgnoreCase))
        : Messages;

    /// <summary>全部消息的终端式纯文本（角色标签+时间戳+内容），供 TextBox 跨行选择</summary>
    public string AllMessagesText
    {
        get
        {
            if (Messages.Count == 0)
                return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (var msg in FilteredMessages)
            {
                var text = msg.CopyAllText;
                if (text.Length > 0)
                {
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
            }
            return sb.Length == 0 ? string.Empty : sb.ToString(0, sb.Length - 2);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredMessages));
        OnPropertyChanged(nameof(AllMessagesText));
    }

    /// <summary>模型下拉选项 — 委托给 ConnectionDropdownManager，供应商切换时清空重填</summary>
    public ObservableCollection<ModelOptionItem> ModelOptions => _connectionDropdown.ModelOptions;

    /// <summary>按 Id O(1) 查找模型项（委托给 ConnectionDropdownManager；null 返回 null）</summary>
    private ModelOptionItem? GetModelById(string? id) => _connectionDropdown.GetModelById(id);

    /// <summary>刷新模型下拉 — 委托给 ConnectionDropdownManager</summary>
    private void RefreshModelOptions()
        => _connectionDropdown.RefreshModelOptions(_session, _modelConfigLoader, SelectedConnection?.Id);

    /// <summary>当前选中的模型下拉项（View 层绑定 ComboBox.SelectedItem）</summary>
    [ObservableProperty]
    private ModelOptionItem? _selectedModelOption;

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

    private int _sessionCounter;

    /// <summary>当前活动会话（首条用户消息后自动设为新标题）</summary>
    private SessionItem? _activeSession;

    /// <summary>已发送消息历史（↑/↓ 回看）</summary>
    private readonly List<string> _inputHistory = [];

    /// <summary>历史回看游标（-1 表示未在回看中）</summary>
    private int _historyIndex = -1;

    /// <summary>是否正处于程序化填充输入（避免手动输入重置游标）</summary>
    private bool _isNavigating;

    /// <summary>UI 对话消息集合（角色化气泡）</summary>
    public ObservableCollection<ChatUiMessage> Messages { get; } = [];

    /// <summary>全局运行状态条（spinner 动词/耗时/token 聚合/后台计数/卡死检测）</summary>
    public GlobalRunStatusViewModel RunStatus { get; } = new();

    /// <summary>
    /// 后台代理管理面板 — 数据源绑定会话门面（引擎运行列表+活跃 fork），
    /// 快照应用时同步 RunStatus 后台计数
    /// </summary>
    public BackgroundAgentsPanelViewModel BackgroundPanel { get; }

    /// <summary>Assistant 消息计数器（CanRegenerate O(1) 查找，由 OnMessagesChanged 维护）</summary>
    private int _assistantMessageCount;

    /// <summary>
    /// UI 消息列表硬上限（G4 内存防护）— 长会话/批量恢复历史时防止集合无限增长。
    /// 仅裁剪显示层，引擎上下文由 /compact 机制管理；TUI 端对应 OutputView 的 2048 行环形缓冲。
    /// </summary>
    public const int MaxVisibleMessages = 500;

    /// <summary>测试可见的助手消息计数（与 _assistantMessageCount 同源，供一致性断言）</summary>
    internal int AssistantMessageCountForTest => _assistantMessageCount;

    /// <summary>消息条数（随集合变化更新，驱动 UI 计数显示）</summary>
    public int MessageCount => Messages.Count;

    /// <summary>是否有消息（驱动空状态引导与清空按钮）</summary>
    public bool HasMessages => Messages.Count > 0;

    /// <summary>会话累计字符数（含输入与回复，粗略 token 估算用）</summary>
    public int TotalChars => Messages.Sum(m => m.Content.Length);

    /// <summary>
    /// 从引擎上下文重读历史并重建消息列表（T1）— 斜杠命令可能改变引擎会话状态
    /// （/resume 装入历史、/clear 清空、/compact 压缩摘要），UI 列表需与引擎保持一致。
    /// 刷新失败仅记日志，不影响命令本身的回显。
    /// </summary>
    /// <param name="echoToKeep">保留在列表末尾的命令回显消息</param>
    private async Task ReloadMessagesFromEngineAsync(ChatUiMessage echoToKeep)
    {
        try
        {
            var records = await _session.GetMessagesAsync(_sendCts?.Token ?? CancellationToken.None);
            Messages.Clear();
            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.Content))
                    continue;
                Messages.Add(new ChatUiMessage
                {
                    Role = MessageRoleExtensions.FromValue(record.Role) ?? MessageRole.User,
                    Content = record.Content,
                    Timestamp = record.Timestamp.ToLocalTime()
                });
            }
            Messages.Add(echoToKeep);
        }
        catch (Exception ex)
        {
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }

    /// <summary>会话导出为文本（`角色 时间: 内容` 格式，供复制/下载）</summary>
    public string ExportSessionText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in Messages)
            {
                sb.Append('[').Append(m.RoleLabel).Append(" · ")
                  .Append(m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine("]");
                sb.AppendLine(m.Content);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    /// <summary>复制当前会话为文本到剪贴板（实际写入由 View 层完成）</summary>
    [RelayCommand]
    private void CopySessionExport() => ExportedSessionCopy = ExportSessionText;

    /// <summary>最近一次会话导出文本（View 层读取后写剪贴板，随后清除）</summary>
    [ObservableProperty]
    private string? _exportedSessionCopy;

    /// <summary>清除会话导出副本（View 写入剪贴板后调用）</summary>
    public void ClearSessionExport() => ExportedSessionCopy = null;

    /// <summary>估算 token 数（中文约 1.6 字符/token，英文约 4 字符/token，取保守下限 4）</summary>
    public int EstimatedTokens => TotalChars / 4;

    /// <summary>
    /// 本轮真实 token 用量文案（如 "Token:1,234"）— 来自引擎 Complete 事件上报的 Usage，
    /// 对齐 TUI statusBar.SetTokenCount；空串表示引擎未上报。
    /// </summary>
    [ObservableProperty]
    private string _tokenUsageText = string.Empty;

    /// <summary>启动 settings.json 文件监控（热重载）— 文件变更时自动刷新供应商/模型列表</summary>
    private void StartModelConfigWatch()
    {
        var path = AppDataConstants.Paths.SettingsFilePath;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !_fileSystem.DirectoryExists(dir))
            return;

        _modelConfigWatcher?.Dispose();
        _modelConfigWatcher = _fileSystem.Watch(dir, System.IO.Path.GetFileName(path));
        _modelConfigWatcher.NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.LastWrite;
        _modelConfigWatcher.DebounceInterval = TimeSpan.FromSeconds(1);
        _modelConfigWatcher.EnableRaisingEvents = true;
        _modelConfigWatcher.DebouncedChanged += OnModelConfigChanged;
        _modelConfigWatcher.DebouncedCreated += OnModelConfigChanged;
    }

    /// <summary>settings.json 变更事件 — 在 UI 线程刷新配置（防抖由 IFileSystemWatcher.DebouncedChanged 接管）</summary>
    private void OnModelConfigChanged(object? sender, FileChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshModelOptionsFromConfig);
    }

    /// <summary>从 settings.json 重新加载配置并刷新连接/模型列表</summary>
    private void RefreshModelOptionsFromConfig()
    {
        try
        {
            // 记住热重载前的选择，重载后尽量保留（避免下拉跳回第一项）
            var previousModelId = SelectedModelOption?.Id;
            var previousConnectionId = SelectedConnection?.Id;

            _session.RefreshVendorModelMap();
            RebuildConnectionOptions();
            RefreshModelOptions();

            // 恢复连接选择（RebuildConnectionOptions 重建了对象引用），用标志位绕过 OnSelectedConnectionChanged 持久化副作用避免循环
            _isRefreshingConfig = true;
            SelectedConnection = GetConnectionById(previousConnectionId)
                ?? GetConnectionById(_session.CurrentVendor)
                ?? _connectionDropdown.ConnectionOptions.FirstOrDefault();
            _isRefreshingConfig = false;
            OnPropertyChanged(nameof(IsMockConnection));

            // 保留当前模型选择（若仍属于当前供应商模型列表），否则取引擎当前模型，再否则取第一个
            SelectedModelOption = GetModelById(previousModelId)
                ?? GetModelById(_session.CurrentModelId)
                ?? ModelOptions.FirstOrDefault();
            SelectedModel = SelectedModelOption?.Id;
            StatusText = "配置已热重载";
        }
        catch (Exception ex)
        {
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }

    /// <summary>启动时从同一 sessions 目录恢复历史会话到侧边栏（CLI 与 GUI 共享会话文件）</summary>
    private void LoadPersistedSessions()
    {
        foreach (var summary in _sessionStore.ListSessions())
        {
            Sessions.Add(new SessionItem
            {
                Id = summary.Id,
                Title = summary.Title
            });
        }
    }

    private void OnMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            _assistantMessageCount = Messages.Count(m => m.Role == MessageRole.Assistant);
        else
        {
            if (e.OldItems is not null)
                foreach (ChatUiMessage m in e.OldItems)
                    if (m.Role == MessageRole.Assistant) _assistantMessageCount--;
            if (e.NewItems is not null)
                foreach (ChatUiMessage m in e.NewItems)
                    if (m.Role == MessageRole.Assistant) _assistantMessageCount++;
        }

        // G4 内存防护：超出上限裁剪最旧消息。RemoveAt 触发的 Remove 事件同步重入本处理器，
        // 计数器随 OldItems 递减，此处无需重复扣减
        while (Messages.Count > MaxVisibleMessages)
            Messages.RemoveAt(0);

        OnPropertyChanged(nameof(MessageCount));
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(TotalChars));
        OnPropertyChanged(nameof(EstimatedTokens));
        OnPropertyChanged(nameof(FilteredMessages));
        OnPropertyChanged(nameof(AllMessagesText));
        if (e.NewItems is not null)
            foreach (ChatUiMessage m in e.NewItems)
                m.PropertyChanged += OnMessagePropertyChanged;
        if (e.OldItems is not null)
            foreach (ChatUiMessage m in e.OldItems)
                m.PropertyChanged -= OnMessagePropertyChanged;
    }

    /// <summary>单条消息属性变化（流式输出 Content 变化）时刷新 AllMessagesText</summary>
    private void OnMessagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatUiMessage.Content) or nameof(ChatUiMessage.ToolResultText))
            OnPropertyChanged(nameof(AllMessagesText));
    }

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

    /// <summary>用户切换模型下拉项时回写共享配置（绑定同一个配置源，下次请求引擎生效）</summary>
    partial void OnSelectedModelOptionChanged(ModelOptionItem? value)
    {
        if (value is not null && value.Id != _session.CurrentModelId)
        {
            SelectedModel = value.Id;
        }
    }

    /// <summary>用户切换模型时持久化由 OnPropertyChanged 自动路由处理（SelectedModel 映射 SetModelAsync）</summary>

    /// <summary>连接下拉候选 — 委托给 ConnectionDropdownManager</summary>
    public IReadOnlyList<ConnectionOptionItem> ConnectionOptions => _connectionDropdown.ConnectionOptions;

    /// <summary>按 Id O(1) 查找连接项（委托给 ConnectionDropdownManager；null 返回 null）</summary>
    private ConnectionOptionItem? GetConnectionById(string? id) => _connectionDropdown.GetConnectionById(id);

    /// <summary>重建连接选项 — 委托给 ConnectionDropdownManager</summary>
    private void RebuildConnectionOptions() => _connectionDropdown.RebuildConnectionOptions(_session);

    /// <summary>当前选中的连接项（切换时替换活动会话，不销毁任何会话）</summary>
    [ObservableProperty]
    private ConnectionOptionItem? _selectedConnection;

    /// <summary>当前是否连接 Mock 引擎（驱动状态提示与 Mock 徽标显隐）</summary>
    public bool IsMockConnection => _session is PlaceholderChatSession;

    partial void OnSelectedConnectionChanged(ConnectionOptionItem? value)
    {
        ViewModelDiagnosticsLogger.WriteDebug($"OnSelectedConnectionChanged: id={value?.Id} refresh={_isRefreshingConfig} realSession={_realSession is not null} session={_session.GetType().Name} currentVendor={_session.CurrentVendor}");
        if (value is null || _isRefreshingConfig)
            return;

        // 无论引擎是否就绪,都持久化供应商切换到 settings.json(PlaceholderChatSession 也能写)
        // 并刷新模型列表供预览
        StatusText = _realSession is not null
            ? $"已连接真实引擎 {value.DisplayText}"
            : $"已选择供应商 {value.DisplayText}（引擎加载中…）";
        try { Task.Run(() => _session.SetVendorAsync(value.Id)).Wait(Timeout); ViewModelDiagnosticsLogger.WriteDebug($"SetVendorAsync ok: id={value.Id}"); }
        catch (Exception ex) { ViewModelDiagnosticsLogger.WriteError(ex); ViewModelDiagnosticsLogger.WriteDebug($"SetVendorAsync FAIL: {ex.Message}"); }

        RefreshModelOptions();
        OnPropertyChanged(nameof(IsMockConnection));
        // 供应商切换后 SetVendorAsync 已把 CurrentModelId 重置为新供应商默认模型，优先匹配它；找不到才取第一个
        SelectedModelOption = GetModelById(_session.CurrentModelId)
            ?? ModelOptions.FirstOrDefault();
        SelectedModel = SelectedModelOption?.Id;
        SelectedEffort = _session.EffortLevel.ToValue();
    }

    /// <summary>输入框变化时同步字符计数并退出历史回看游标（斜杠刷新由 View 层防抖触发）</summary>
    partial void OnInputTextChanged(string value)
    {
        CharsCount = value.Length;
        OnPropertyChanged(nameof(IsInputTooLong));
        if (!_isNavigating)
            _historyIndex = -1;
    }

    /// <summary>新建一个会话（加入侧边栏并选中）</summary>
    [RelayCommand]
    private void NewConversation()
    {
        _sessionCounter++;
        var item = new SessionItem
        {
            Title = $"会话 {_sessionCounter}",
            IsSelected = true
        };        foreach (var s in Sessions)
            s.IsSelected = false;
        Sessions.Add(item);
        _activeSession = item;
        Messages.Clear();
        _session.SwitchSession(item.Id);
        OnPropertyChanged(nameof(Sessions));
    }

    /// <summary>切换深浅主题（占位阶段仅记录状态，UI 由 View 层响应）</summary>
    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    /// <summary>当前选中的会话（供视图侧边栏选中/删除定位）</summary>
    public SessionItem? SelectedSession => Sessions.FirstOrDefault(s => s.IsSelected);

    /// <summary>复制指定消息的完整终端式文本（含思考/工具/diff，穿透容器标签）到剪贴板并标记反馈状态</summary>
    [RelayCommand]
    private void CopyMessage(ChatUiMessage? message)
    {
        if (message is null || string.IsNullOrEmpty(message.CopyAllText))
            return;
        CopiedMessageCopy = message.CopyAllText;
        CopiedMessage = message.Timestamp.GetHashCode();
        // 剪贴板实际写入由 View 层完成（消费 CopiedMessageCopy），此处仅驱动状态提示
    }

    /// <summary>最近一次待复制消息的完整文本（View 层读取后写剪贴板，随后清除）</summary>
    [ObservableProperty]
    private string? _copiedMessageCopy;

    /// <summary>清除待复制消息文本（View 写入剪贴板后调用）</summary>
    public void ClearCopiedMessageCopy() => CopiedMessageCopy = null;

    /// <summary>删除单条消息</summary>
    [RelayCommand]
    private void RemoveMessage(ChatUiMessage? message)
    {
        if (message is not null)
            Messages.Remove(message);
    }

    /// <summary>将当前会话消息持久化到 ~/.jcc/sessions/{Id}.json（含自动命名标题）</summary>
    private void SaveActiveSession()
    {
        if (_activeSession is null)
            return;

        var data = new Persistence.GuiSessionData
        {
            Id = _activeSession.Id,
            CustomTitle = _activeSession.Title,
            CreatedAt = DateTime.UtcNow,
            Messages = Messages
                .Where(m => m.Role is MessageRole.User or MessageRole.Assistant && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => new Persistence.GuiSessionMessage
                {
                    Role = m.Role.ToValue(),
                    Content = m.Content,
                    Timestamp = m.Timestamp
                })
                .ToList()
        };

        try
        {
            _sessionStore.Save(data);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 会话持久化失败: {ex.Message}");
        }
    }

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

    /// <summary>撤回上一轮回复并重新生成（基于最后一条用户消息）</summary>
    [RelayCommand]
    private async Task RegenerateLastReplyAsync()
    {
        if (IsBusy)
            return;

        var lastUser = Messages.LastOrDefault(m => m.Role == MessageRole.User && !string.IsNullOrWhiteSpace(m.Content));
        if (lastUser is null)
            return;
        var lastUserIndex = Messages.IndexOf(lastUser);

        await _session.RewindLastTurnAsync();
        while (Messages.Count > lastUserIndex)
            Messages.RemoveAt(Messages.Count - 1);

        InputText = lastUser.Content;
        await SendAsync();
    }

    /// <summary>是否有可重新生成的上一轮回复（O(1) 计数器查找）</summary>
    public bool CanRegenerate => _assistantMessageCount > 0;

    [RelayCommand]
    private Task ClearHistoryAsync()
        => ClearHistoryInternalAsync();

    private async Task ClearHistoryInternalAsync()
    {
        Messages.Clear();
        await _session.ClearHistoryAsync();
    }

    /// <summary>展开/收拢右侧设置面板</summary>
    [RelayCommand]
    private void ToggleSettingsPanel() => IsSettingsPanelOpen = !IsSettingsPanelOpen;

    /// <summary>清空全部会话（会话列表与消息一并重置，持久化文件同步删除）</summary>
    [RelayCommand]
    private void ClearAllSessions()
    {
        foreach (var s in Sessions.ToList())
        {
            try
            {
                _sessionStore.Delete(s.Id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 会话删除失败: {ex.Message}");
            }
        }
        Sessions.Clear();
        Messages.Clear();
        _sessionCounter = 0;
        NewConversation();
    }

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

    /// <summary>从会话列表删除指定会话（同步删除持久化文件）</summary>
    [RelayCommand]
    private void RemoveSession(SessionItem? session)
    {
        if (session is null)
            return;
        Sessions.Remove(session);
        try
        {
            _sessionStore.Delete(session.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 会话删除失败: {ex.Message}");
        }
        if (session.IsSelected && Sessions.Count > 0)
            Sessions[^1].IsSelected = true;
    }

    /// <summary>选中指定会话（单击切换当前会话，同一时刻仅一个选中；未选中态用作未可选区分）</summary>
    [RelayCommand]
    private async Task SelectSession(SessionItem? session)
    {
        if (session is null)
            return;
        if (session == _activeSession)
            return;

        foreach (var s in Sessions)
            s.IsSelected = s == session;
        _activeSession = session;
        _session.SwitchSession(session.Id);

        // 需求11：子会话点击展示内容（SubSessionMessages 缓存或引擎加载）
        if (session.IsSubSession)
        {
            await LoadSubSessionContentAsync(session);
            return;
        }

        // 切换会话时从持久化恢复该会话消息到消息区（空会话则清空）
        var data = _sessionStore.Load(session.Id);
        Messages.Clear();
        var historyForEngine = new List<(MessageRole Role, string Content)>();
        if (data is not null)
        {
            foreach (var msg in data.Messages)
            {
                if (string.IsNullOrWhiteSpace(msg.Content))
                    continue;
                var role = MessageRoleExtensions.FromValue(msg.Role) ?? MessageRole.User;
                Messages.Add(new ChatUiMessage
                {
                    Role = role,
                    Content = msg.Content,
                    Timestamp = msg.Timestamp
                });
                historyForEngine.Add((role, msg.Content));
            }
        }

        // 把持久化历史灌入底层引擎上下文 — GUI 新进程 StateService 内存为空，
        // SwitchSession 仅切换 sessionId 不加载历史，需显式灌入否则发送时 LLM 收不到历史
        await _session.LoadHistoryAsync(historyForEngine);
    }

    /// <summary>重命名指定会话（标题由视图双击触发，空标题忽略）</summary>
    [RelayCommand]
    private void RenameSession(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;
        var active = SelectedSession;
        if (active is not null)
            active.Title = title.Trim();
    }

    /// <summary>进入重命名编辑态（双击会话条目）</summary>
    [RelayCommand]
    private void BeginRenameSession(SessionItem? session)
    {
        if (session is null)
            return;
        foreach (var s in Sessions)
            s.IsSelected = s == session;
        session.IsRenaming = true;
        session.RenameDraft = session.Title;
    }

    /// <summary>提交重命名（Enter 触发），空标题则保留原名</summary>
    [RelayCommand]
    private void CommitRenameSession(SessionItem? session)
    {
        if (session is null)
            return;
        session.IsRenaming = false;
        if (!string.IsNullOrWhiteSpace(session.RenameDraft))
            session.Title = session.RenameDraft.Trim();
    }

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

    /// <summary>取消重命名（Esc 触发），恢复原标题</summary>
    [RelayCommand]
    private void CancelRenameSession(SessionItem? session)
    {
        if (session is not null)
            session.IsRenaming = false;
    }

    /// <summary>向输入框追加文本（光标定位到内容尾部）</summary>
    private void ConcatInput(string text) => InputText += text;

    /// <summary>用首条用户消息为会话自动命名（截断避免过长）</summary>
    private void RenameActiveSessionTo(string message)
    {
        if (_activeSession is null)
            return;
        var title = message.Trim().Length > 18
            ? message.Trim()[..18] + "…"
            : message.Trim();
        if (title.Length > 0)
            _activeSession.Title = title;
    }

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
