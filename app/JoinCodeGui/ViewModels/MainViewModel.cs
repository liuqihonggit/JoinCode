using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JoinCode.Abstractions.Configuration.Llm;
using JoinCode.Abstractions.Configuration.Providers;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.UI;
using JoinCode.Gui.Hosting;

namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 主窗口 ViewModel — 承载引擎会话门面与基础对话占位。
/// 依赖注入仅走 <see cref="IJccChatSession"/>，不触碰引擎内部实现。
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private IJccChatSession? _realSession;
    private IJccChatSession? _mockSession;
    private IJccChatSession _session;
    private readonly Persistence.GuiSessionStore _sessionStore;
    private readonly Persistence.GuiPreferencesStore _preferencesStore;
    /// <summary>独立配置服务 — 引擎加载失败时仍可持久化 settings.json（主题/供应商/模型/推理力度）</summary>
    private readonly IConfigurationService _configService;
    private bool _isPreferencesLoaded;
    private bool _isRefreshingConfig;
    private bool _isApplyingExternalTheme;
    private System.IO.FileSystemWatcher? _modelConfigWatcher;
    private DateTime _lastConfigReload = DateTime.MinValue;

    /// <summary>异步操作硬超时（防止命令续体在单线程上下文死锁）</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>属性名→持久化操作映射 — OnPropertyChanged 自动路由统一持久化（新增可持久化属性只需 RegisterPersist 一行）</summary>
    private readonly Dictionary<string, Action> _persistActions = new(StringComparer.Ordinal);

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

    /// <summary>模型下拉选项缓存 — session 切换时失效重建，避免每次访问重建数组导致 ComboBox 选中项引用失效闪现</summary>
    /// <summary>模型下拉选项 — ObservableCollection 双向绑定，供应商切换时清空重填</summary>
    public ObservableCollection<ModelOptionItem> ModelOptions { get; } = [];

    /// <summary>刷新模型下拉 — 从 VendorModelMap 取当前供应商模型列表填充 ObservableCollection</summary>
    private void RefreshModelOptions()
    {
        var provider = SelectedConnection?.Id ?? _session.CurrentVendor;
        var providerDisplay = VendorKindExtensions.FromValue(provider)?.ToString() ?? provider;
        var map = _session.VendorModelMap;
        var source = map.TryGetValue(provider, out var models) && models is not null
            ? models.ToList()
            : new List<string>();
        var current = _session.CurrentModelId;
        if (!string.IsNullOrWhiteSpace(current)
            && source.All(id => !string.Equals(id, current, StringComparison.OrdinalIgnoreCase)))
        {
            var modelProvider = ModelConfigLoader.FindProviderByModelId(current);
            if (modelProvider is null || string.Equals(modelProvider, provider, StringComparison.OrdinalIgnoreCase))
            {
                source.Add(current);
            }
        }
        ModelOptions.Clear();
        foreach (var id in source)
            ModelOptions.Add(new ModelOptionItem(id, $"{providerDisplay}:{id}"));
    }

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

    private int _sessionCounter;

    /// <summary>当前活动会话（首条用户消息后自动设为新标题）</summary>
    private SessionItem? _activeSession;

    /// <summary>当前生成任务的取消源（停止生成用）</summary>
    private System.Threading.CancellationTokenSource? _sendCts;

    /// <summary>已发送消息历史（↑/↓ 回看）</summary>
    private readonly List<string> _inputHistory = [];

    /// <summary>历史回看游标（-1 表示未在回看中）</summary>
    private int _historyIndex = -1;

    /// <summary>是否正处于程序化填充输入（避免手动输入重置游标）</summary>
    private bool _isNavigating;

    /// <summary>UI 对话消息集合（角色化气泡）</summary>
    public ObservableCollection<ChatUiMessage> Messages { get; } = [];

    /// <summary>Assistant 消息计数器（CanRegenerate O(1) 查找，由 OnMessagesChanged 维护）</summary>
    private int _assistantMessageCount;

    /// <summary>消息条数（随集合变化更新，驱动 UI 计数显示）</summary>
    public int MessageCount => Messages.Count;

    /// <summary>是否有消息（驱动空状态引导与清空按钮）</summary>
    public bool HasMessages => Messages.Count > 0;

    /// <summary>会话累计字符数（含输入与回复，粗略 token 估算用）</summary>
    public int TotalChars => Messages.Sum(m => m.Content.Length);

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

    public MainViewModel(IJccChatSession? session = null, Persistence.GuiSessionStore? store = null, Persistence.GuiPreferencesStore? preferencesStore = null)
    {
        _realSession = session;
        _configService = new Core.Configuration.ConfigurationService(new IO.FileSystem.PhysicalFileSystem());
        _session = session ?? new Hosting.PlaceholderChatSession(_configService);
        _sessionStore = store ?? new Persistence.GuiSessionStore(new IO.FileSystem.PhysicalFileSystem());
        _preferencesStore = preferencesStore ?? new Persistence.GuiPreferencesStore(new IO.FileSystem.PhysicalFileSystem());
        _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;
        _selectedEffort = _session.EffortLevel.ToValue();
        Messages.CollectionChanged += OnMessagesChanged;
        LoadPersistedSessions();
        NewConversation();

        // 注册持久化路由（在 LoadPreferences 之前，加载期 _isPreferencesLoaded=false 不触发）
        RegisterPersistActions();

        // 加载 GUI 偏好并应用到 UI 属性（启动时恢复上次显示的内容）
        LoadPreferences();

        if (session is not null)
        {
            RebuildConnectionOptions();
            RefreshModelOptions();
            _selectedModel = _session.CurrentModelId;
            _selectedModelOption = ModelOptions.FirstOrDefault(m => m.Id == _session.CurrentModelId);
            _selectedConnection = _connectionOptions.FirstOrDefault(c => c.Id == session.CurrentVendor)
                ?? _connectionOptions.FirstOrDefault();
            IsEngineLoaded = true;
            StartModelConfigWatch();
            // 引擎就绪后把偏好里的采样参数应用到引擎
            ApplyPreferencesToEngine();
            // 订阅 settings.json theme 变更 + 从 settings.json 读主题（唯一数据源，对齐 CLI /theme）
            session.ThemeChanged += OnThemeChanged;
            LoadThemeFromSettings();
        }
        else
        {
            StatusText = "正在加载引擎…";
            RebuildConnectionOptions();
            RefreshModelOptions();
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
    public void AttachRealSession(IJccChatSession session)
    {
        WriteDebugLog($"AttachRealSession: currentVendor={session.CurrentVendor} currentModel={session.CurrentModelId}");
        _realSession = session;
        _session = session;
        _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;

        RebuildConnectionOptions();
        RefreshModelOptions();
        SelectedModel = _session.CurrentModelId;
        SelectedModelOption = ModelOptions.FirstOrDefault(m => m.Id == _session.CurrentModelId);
        SelectedEffort = _session.EffortLevel.ToValue();
        _isRefreshingConfig = true;
        SelectedConnection = _connectionOptions.FirstOrDefault(c => c.Id == session.CurrentVendor)
            ?? _connectionOptions.FirstOrDefault();
        _isRefreshingConfig = false;
        WriteDebugLog($"AttachRealSession: SelectedConnection={SelectedConnection?.Id}");

        // 清空延迟构建的斜杠命令缓存，改用真实引擎的命令清单
        _slashCommandCache = null;
        RefreshSlashSuggestions();

        OnPropertyChanged(nameof(IsMockConnection));
        IsEngineLoaded = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var tools = await session.GetAvailableToolsAsync().WaitAsync(Timeout);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _availableToolsCache = tools);
            }
            catch (Exception ex)
            {
                WriteErrorLog(ex);
            }
        });
        StatusText = $"已连接真实引擎 {session.CurrentVendor}";
        StartModelConfigWatch();
        // 引擎热切换后把偏好里的采样参数应用到引擎
        ApplyPreferencesToEngine();
        // 订阅 settings.json theme 变更（外部 CLI /theme 驱动 GUI 热重载）+ 从 settings.json 读主题
        session.ThemeChanged += OnThemeChanged;
        LoadThemeFromSettings();
    }

    /// <summary>启动 models.json 用户覆盖文件监控（热重载）— 文件变更时自动刷新供应商/模型列表</summary>
    private void StartModelConfigWatch()
    {
        var path = ModelConfigLoader.GetUserOverridePath();
        var dir = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
            return;

        _modelConfigWatcher?.Dispose();
#pragma warning disable JCC9005
        _modelConfigWatcher = new System.IO.FileSystemWatcher(dir, System.IO.Path.GetFileName(path))
        {
            NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
#pragma warning restore JCC9005
        _modelConfigWatcher.Changed += OnModelConfigChanged;
        _modelConfigWatcher.Created += OnModelConfigChanged;
    }

    /// <summary>models.json 变更事件 — 防抖 1s 后在 UI 线程刷新配置</summary>
    private void OnModelConfigChanged(object sender, System.IO.FileSystemEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastConfigReload).TotalMilliseconds < 1000)
            return;
        _lastConfigReload = now;
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshModelOptionsFromConfig);
    }

    /// <summary>从 models.json 重新加载配置并刷新连接/模型列表</summary>
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
            SelectedConnection = _connectionOptions.FirstOrDefault(c => c.Id == previousConnectionId)
                ?? _connectionOptions.FirstOrDefault(c => c.Id == _session.CurrentVendor)
                ?? _connectionOptions.FirstOrDefault();
            _isRefreshingConfig = false;
            OnPropertyChanged(nameof(IsMockConnection));

            // 保留当前模型选择（若仍属于当前供应商模型列表），否则取引擎当前模型，再否则取第一个
            SelectedModelOption = ModelOptions.FirstOrDefault(m => string.Equals(m.Id, previousModelId, StringComparison.OrdinalIgnoreCase))
                ?? ModelOptions.FirstOrDefault(m => string.Equals(m.Id, _session.CurrentModelId, StringComparison.OrdinalIgnoreCase))
                ?? ModelOptions.FirstOrDefault();
            SelectedModel = SelectedModelOption?.Id;
            StatusText = "配置已热重载";
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
        }
    }

    /// <summary>引擎加载失败时回退到 Mock 引擎（IsMockConnection 驱动按钮状态，供应商下拉保持真实列表）</summary>
    public void FallbackToMock()
    {
        _session = _mockSession ??= new Hosting.PlaceholderChatSession(_configService);
        _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;
        RebuildConnectionOptions();
        SelectedConnection = _connectionOptions.FirstOrDefault();
        RefreshModelOptions();
        SelectedModel = _session.CurrentModelId;
        SelectedModelOption = ModelOptions.FirstOrDefault(m => m.Id == _session.CurrentModelId);
        IsEngineLoaded = true;
        // 引擎失败回退后仍从 settings.json 读主题（PlaceholderChatSession 可读写 settings.json）
        LoadThemeFromSettings();
    }

    /// <summary>斜杠命令缓存（懒加载；空命令时回退内置高频命令列表）</summary>
    private IReadOnlyList<SlashCommandItem>? _slashCommandCache;

    /// <summary>引擎可用工具缓存 — AttachRealSession 时异步加载，#工具补全消费</summary>
    private IReadOnlyList<ToolSummary>? _availableToolsCache;

    /// <summary>当前光标位置（由 View 层同步，用于解析斜杠命令前缀）</summary>
    [ObservableProperty]
    private int _inputCaretIndex;

    /// <summary>最近一次斜杠解析结果（回填替换区间用）</summary>
    private SlashParseResult _slashParseResult;

    /// <summary>当前输入的斜杠命令过滤建议（驱动内联补全下拉）</summary>
    public ObservableCollection<SlashCommandItem> SlashSuggestions { get; } = [];

    /// <summary>斜杠命令补全下拉是否打开（解析触发且有匹配命令时）</summary>
    public bool IsSlashPopupOpen => _slashParseResult.ShouldComplete && SlashSuggestions.Count > 0;

    /// <summary>斜杠建议当前选中索引（↑↓ 导航）</summary>
    [ObservableProperty]
    private int _slashSelectedIndex = -1;

    /// <summary>刷新斜杠命令建议 — 由 View 层防抖后调用，用光标解析 + Trie 匹配 + 排序</summary>
    public void RefreshSlashSuggestions()
    {
        if (IsBusy)
        {
            ClearSlashSuggestions();
            return;
        }

        _slashParseResult = SlashCommandParser.Parse(InputText, InputCaretIndex);
        if (!_slashParseResult.ShouldComplete)
        {
            ClearSlashSuggestions();
            return;
        }

        if (_slashParseResult.Mode == SlashCompletionMode.Argument)
        {
            RefreshArgumentSuggestions();
            return;
        }

        if (_slashParseResult.Mode == SlashCompletionMode.File)
        {
            RefreshFileSuggestions();
            return;
        }

        if (_slashParseResult.Mode == SlashCompletionMode.Tool)
        {
            RefreshToolSuggestions();
            return;
        }

        var cache = _slashCommandCache ??= BuildSlashCommandCache();
        var matched = SlashCommandItem.Filter(_slashParseResult.Prefix, cache);
        var ranked = SlashCommandRanker.Rank(matched, _slashParseResult.Prefix);

        SlashSuggestions.Clear();
        var prefixLen = _slashParseResult.Prefix.Length;
        foreach (var item in ranked)
        {
            item.MatchedPart = item.Name.Length >= prefixLen ? item.Name[..prefixLen] : item.Name;
            item.RemainingPart = item.Name.Length >= prefixLen ? item.Name[prefixLen..] : string.Empty;
            SlashSuggestions.Add(item);
        }
        SlashSelectedIndex = SlashSuggestions.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(IsSlashPopupOpen));
    }

    /// <summary>刷新命令参数补全候选 — 由 CommandArgumentProvider 按命令名提供参数列表</summary>
    private void RefreshArgumentSuggestions()
    {
        var args = CommandArgumentProvider.GetArguments(
            _slashParseResult.CommandName, _slashParseResult.ArgumentPrefix, _session);

        SlashSuggestions.Clear();
        var prefixLen = _slashParseResult.ArgumentPrefix.Length;
        foreach (var item in args)
        {
            item.MatchedPart = item.Name.Length >= prefixLen ? item.Name[..prefixLen] : item.Name;
            item.RemainingPart = item.Name.Length >= prefixLen ? item.Name[prefixLen..] : string.Empty;
            SlashSuggestions.Add(item);
        }
        SlashSelectedIndex = SlashSuggestions.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(IsSlashPopupOpen));
    }

    /// <summary>刷新文件补全候选 — 由 FileCompletionProvider 扫描当前工作目录</summary>
    private void RefreshFileSuggestions()
    {
        var files = FileCompletionProvider.GetFiles(_slashParseResult.Prefix);
        PopulateSuggestions(files, _slashParseResult.Prefix);
    }

    /// <summary>刷新工具补全候选 — 由 ToolCompletionProvider 提供工具列表</summary>
    private void RefreshToolSuggestions()
    {
        var tools = ToolCompletionProvider.GetTools(_slashParseResult.Prefix, _availableToolsCache);
        PopulateSuggestions(tools, _slashParseResult.Prefix);
    }

    /// <summary>填充补全候选列表（高亮匹配前缀）</summary>
    private void PopulateSuggestions(IReadOnlyList<SlashCommandItem> candidates, string prefix)
    {
        SlashSuggestions.Clear();
        var prefixLen = prefix.Length;
        foreach (var item in candidates)
        {
            item.MatchedPart = item.Name.Length >= prefixLen ? item.Name[..prefixLen] : item.Name;
            item.RemainingPart = item.Name.Length >= prefixLen ? item.Name[prefixLen..] : string.Empty;
            SlashSuggestions.Add(item);
        }
        SlashSelectedIndex = SlashSuggestions.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(IsSlashPopupOpen));
    }

    /// <summary>清空斜杠建议并关闭面板</summary>
    private void ClearSlashSuggestions()
    {
        _slashParseResult = SlashParseResult.None;
        SlashSuggestions.Clear();
        SlashSelectedIndex = -1;
        OnPropertyChanged(nameof(IsSlashPopupOpen));
    }

    /// <summary>关闭斜杠补全面板（Esc 调用，不清空输入框文本）</summary>
    public void CloseSlashPopup() => ClearSlashSuggestions();

    /// <summary>构建斜杠命令缓存 — 引擎命令为空时回退内置高频子集</summary>
    private IReadOnlyList<SlashCommandItem> BuildSlashCommandCache()
    {
        var metadata = GetAvailableSlashCommands();
        return metadata.Count > 0 ? SlashCommandItem.FromMetadata(metadata) : SlashCommandItem.BuiltInCommands;
    }

    /// <summary>使斜杠命令缓存失效 — 运行时动态增删命令后调用，下次刷新时重建 Trie</summary>
    public void InvalidateSlashCommandCache() => _slashCommandCache = null;

    /// <summary>完成斜杠命令补全 — 将选中命令回填到光标位置（替换 / 到前缀结束区间），不破坏其他文本</summary>
    public void CompleteSlashSuggestion()
    {
        if (SlashSelectedIndex < 0 || SlashSelectedIndex >= SlashSuggestions.Count)
            return;
        if (!_slashParseResult.ShouldComplete)
            return;

        var item = SlashSuggestions[SlashSelectedIndex];

        if (_slashParseResult.Mode == SlashCompletionMode.Argument)
        {
            var argStart = _slashParseResult.ArgumentStart;
            var prefixEnd = _slashParseResult.PrefixEnd;
            InputText = InputText[..argStart] + item.Name + InputText[prefixEnd..];
            InputCaretIndex = argStart + item.Name.Length;
        }
        else if (_slashParseResult.Mode == SlashCompletionMode.File ||
                 _slashParseResult.Mode == SlashCompletionMode.Tool)
        {
            var triggerEnd = _slashParseResult.SlashIndex + 1;
            var prefixEnd = _slashParseResult.PrefixEnd;
            InputText = InputText[..triggerEnd] + item.Name + " " + InputText[prefixEnd..];
            InputCaretIndex = triggerEnd + item.Name.Length + 1;
        }
        else
        {
            var slashIndex = _slashParseResult.SlashIndex;
            var prefixEnd = _slashParseResult.PrefixEnd;
            InputText = InputText[..slashIndex] + item.Name + " " + InputText[prefixEnd..];
            InputCaretIndex = slashIndex + item.Name.Length + 1;
        }
        ClearSlashSuggestions();
    }

    /// <summary>斜杠建议导航（↑ 传 -1，↓ 传 1），到顶/到底不再移动（不循环回绕）</summary>
    public void SlashNavigate(int delta)
    {
        if (SlashSuggestions.Count == 0)
            return;
        SlashSelectedIndex = Math.Clamp(SlashSelectedIndex + delta, 0, SlashSuggestions.Count - 1);
    }

    /// <summary>
    /// 权限确认回调 — 由 View 层注入（弹窗实现），引擎权限待确认时调用。
    /// 未注入时默认拒绝（等价于 Deny），保证无弹窗环境下引擎行为可预期。
    /// </summary>
    public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmCallback { get; set; }

    /// <summary>网关权限确认请求 → 委托给 View 层弹窗回调；未注入回调时默认拒绝</summary>
    private Task<PermissionConfirmationDecision> OnPermissionConfirmationRequestedAsync(PermissionConfirmationRequest request)
        => PermissionConfirmCallback is not null
            ? PermissionConfirmCallback(request)
            : Task.FromResult(PermissionConfirmationDecision.Deny);

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

    partial void OnMaxTokensChanged(int value)
    {
        OnPropertyChanged(nameof(IsInputTooLong));
        WriteBackTemperatureAndMaxTokens();
    }

    partial void OnTemperatureChanged(double value)
    {
        WriteBackTemperatureAndMaxTokens();
    }

    /// <summary>系统提示词变更时应用到引擎（持久化由 OnPropertyChanged 自动路由处理）</summary>
    partial void OnSystemPromptChanged(string value)
    {
        if (_isPreferencesLoaded && !string.IsNullOrWhiteSpace(value))
        {
            _ = Task.Run(async () =>
            {
                try { await _session.SetSystemPromptAsync(value).WaitAsync(Timeout); }
                catch (Exception ex) { WriteErrorLog(ex); }
            });
        }
    }

    /// <summary>
    /// 滑块变更写回引擎会话 — 经门面 SetTemperatureAsync/SetMaxTokensAsync 写入共享
    /// ExecutionSettingsProvider，ChatOptionsFactory 下次创建即覆盖默认值（对齐 CLI 语义：不持久化）。
    /// </summary>
    private void WriteBackTemperatureAndMaxTokens()
    {
        Task.Run(async () =>
        {
            try
            {
                await _session.SetTemperatureAsync((float)Temperature).WaitAsync(Timeout);
                await _session.SetMaxTokensAsync(MaxTokens).WaitAsync(Timeout);
                StatusText = $"采样参数: 温度 {Temperature:0.00}, 最大 {MaxTokens} tokens";
            }
            catch (Exception ex)
            {
                StatusText = $"设置采样参数失败: {ex.Message}";
            }
        });
    }

    /// <summary>
    /// 加载 GUI 偏好并应用到 UI 属性 — 启动时恢复上次显示的内容。
    /// 加载期间置 _isPreferencesLoaded=false 防止 OnXxxChanged 回写磁盘。
    /// </summary>
    private void LoadPreferences()
    {
        try
        {
            var prefs = _preferencesStore.Load();
            _isPreferencesLoaded = false;
            Temperature = prefs.Temperature;
            MaxTokens = prefs.MaxTokens;
            SystemPrompt = prefs.SystemPrompt;
            FontSize = prefs.FontSize;
            StreamingEnabled = prefs.StreamingEnabled;
            _isPreferencesLoaded = true;
        }
        catch (Exception ex)
        {
            _isPreferencesLoaded = true;
            WriteErrorLog(ex);
        }
    }

    /// <summary>把偏好里的采样参数应用到引擎（构造函数引擎就绪后 / AttachRealSession 热切换后调用）</summary>
    private void ApplyPreferencesToEngine()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _session.SetTemperatureAsync((float)Temperature).WaitAsync(Timeout);
                await _session.SetMaxTokensAsync(MaxTokens).WaitAsync(Timeout);
                if (!string.IsNullOrWhiteSpace(SystemPrompt))
                    await _session.SetSystemPromptAsync(SystemPrompt).WaitAsync(Timeout);
            }
            catch (Exception ex)
            {
                WriteErrorLog(ex);
            }
        });
    }

    /// <summary>
    /// 同步阻塞 UI 线程至异步持久化操作完成 — Task.Run 避免 UI 线程 SynchronizationContext 死锁，
    /// Wait(Timeout) 确保点击时立即落盘，关闭 GUI 不丢失。失败写错误日志不抛出。
    /// </summary>
    private void PersistSync(Func<Task> action)
    {
        try
        {
            Task.Run(action).Wait(Timeout);
            WriteDebugLog($"PersistSync ok");
        }
        catch (Exception ex) { WriteErrorLog(ex); WriteDebugLog($"PersistSync FAIL: {ex.Message}"); }
    }

    /// <summary>写诊断日志到 dumps/persist_debug.log（定位持久化路由问题）</summary>
    private static void WriteDebugLog(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "dumps");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "persist_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch (Exception writeEx)
        {
            System.Console.Error.WriteLine($"无法写入诊断日志: {writeEx.Message}");
        }
    }

    /// <summary>
    /// 注册属性名→持久化操作映射 — 构造函数调用一次。简单属性全走路由，
    /// 复杂属性（SelectedConnection/SelectedEffort）保留 OnXxxChanged 手动调 PersistSync。
    /// </summary>
    private void RegisterPersistActions()
    {
        _persistActions[nameof(IsDarkTheme)] = () =>
            PersistSync(() => _session.SetThemeAsync(IsDarkToTheme(IsDarkTheme)));
        _persistActions[nameof(SelectedModel)] = () =>
        {
            var m = SelectedModel;
            if (!string.IsNullOrWhiteSpace(m) && !string.Equals(m, _session.CurrentModelId, StringComparison.Ordinal))
                PersistSync(() => _session.SetModelAsync(m!));
        };
        _persistActions[nameof(Temperature)] = SavePreferences;
        _persistActions[nameof(MaxTokens)] = SavePreferences;
        _persistActions[nameof(SystemPrompt)] = SavePreferences;
        _persistActions[nameof(FontSize)] = SavePreferences;
        _persistActions[nameof(StreamingEnabled)] = SavePreferences;
    }

    /// <summary>
    /// PropertyChanged 自动路由 — 拦截所有属性变更，查 _persistActions 字典统一持久化。
    /// 标志位过滤：_isPreferencesLoaded（加载期不回写）、_isRefreshingConfig（热重载期不回写）、
    /// _isApplyingExternalTheme（外部主题变更不回写避免循环）。
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        var propertyName = e.PropertyName;
        if (propertyName is null)
            return;
        if (_persistActions.ContainsKey(propertyName))
            WriteDebugLog($"OnPropertyChanged: {propertyName} | loaded={_isPreferencesLoaded} refresh={_isRefreshingConfig} extTheme={_isApplyingExternalTheme} | session={_session.GetType().Name}");
        if (!_isPreferencesLoaded || _isRefreshingConfig || _isApplyingExternalTheme)
            return;
        if (_persistActions.TryGetValue(propertyName, out var action))
            action();
    }

    /// <summary>保存当前 UI 偏好到磁盘 — 各 OnXxxChanged 调用，_isPreferencesLoaded 防止初始化时回写</summary>
    private void SavePreferences()
    {
        if (!_isPreferencesLoaded)
            return;
        try
        {
            _preferencesStore.Save(new Persistence.GuiPreferences
            {
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                SystemPrompt = SystemPrompt,
                FontSize = FontSize,
                StreamingEnabled = StreamingEnabled
            });
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
        }
    }

    /// <summary>
    /// ThemeKind → bool IsDarkTheme 映射 — auto 按时间（6-18 点 light，否则 dark），
    /// daltonized/ansi 降级为基础明暗（GUI 调色板暂不支持色盲友好变体）。
    /// </summary>
    private static bool ThemeToIsDark(ThemeKind theme)
    {
        return theme switch
        {
            ThemeKind.Dark or ThemeKind.DarkDaltonized or ThemeKind.DarkAnsi => true,
            ThemeKind.Light or ThemeKind.LightDaltonized or ThemeKind.LightAnsi => false,
            ThemeKind.Auto => DateTime.Now.Hour is < 6 or >= 18,
            _ => true
        };
    }

    /// <summary>bool IsDarkTheme → ThemeKind 映射 — GUI 仅暴露 dark/light 二态</summary>
    private static ThemeKind IsDarkToTheme(bool isDark) => isDark ? ThemeKind.Dark : ThemeKind.Light;

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
                    IsDarkTheme = ThemeToIsDark(theme);
                    _isApplyingExternalTheme = false;
                });
            }
            catch (Exception ex)
            {
                WriteErrorLog(ex);
            }
        });
    }

    /// <summary>settings.json theme 外部变更事件处理 — 驱动 GUI 热重载（双向绑定）</summary>
    private void OnThemeChanged(object? sender, ThemeKind theme)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _isApplyingExternalTheme = true;
            IsDarkTheme = ThemeToIsDark(theme);
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

    /// <summary>Mock 引擎连接候选（始终存在于下拉列表，用于演示/本地验证）</summary>
    private static readonly ConnectionOptionItem MockConnection = new()
    {
        Id = "mock",
        DisplayText = "🧪 Mock 引擎（演示）",
        IsMock = true
    };

    /// <summary>连接下拉候选 — ObservableCollection 绑定 ComboBox，引用固定不丢失选中项</summary>
    private readonly ObservableCollection<ConnectionOptionItem> _connectionOptions = [];

    /// <summary>连接下拉候选 — Mock 引擎 + 配置文件驱动的全部供应商（改 config 自动更新）</summary>
    public IReadOnlyList<ConnectionOptionItem> ConnectionOptions => _connectionOptions;

    /// <summary>重建连接选项 — 从 VendorModelMap.Keys 填充 ObservableCollection（纯真实供应商，Mock 由独立按钮切换）</summary>
    private void RebuildConnectionOptions()
    {
        _connectionOptions.Clear();
        foreach (var provider in _session.VendorModelMap.Keys)
        {
            var display = VendorKindExtensions.FromValue(provider)?.ToString() ?? provider;
            _connectionOptions.Add(new ConnectionOptionItem
            {
                Id = provider,
                DisplayText = display,
                IsMock = false
            });
        }
    }

    /// <summary>当前选中的连接项（切换时替换活动会话，不销毁任何会话）</summary>
    [ObservableProperty]
    private ConnectionOptionItem? _selectedConnection;

    /// <summary>当前是否连接 Mock 引擎（驱动状态提示与 Mock 徽标显隐）</summary>
    public bool IsMockConnection => _session is PlaceholderChatSession;

    partial void OnSelectedConnectionChanged(ConnectionOptionItem? value)
    {
        WriteDebugLog($"OnSelectedConnectionChanged: id={value?.Id} refresh={_isRefreshingConfig} realSession={_realSession is not null} session={_session.GetType().Name} currentVendor={_session.CurrentVendor}");
        if (value is null || _isRefreshingConfig)
            return;

        // Mock 模式下不处理供应商下拉切换（Mock 由独立按钮控制）
        if (_session is PlaceholderChatSession)
            return;

        if (_realSession is null)
        {
            WriteDebugLog($"OnSelectedConnectionChanged: 引擎未就绪,跳过持久化");
            return;
        }

        StatusText = $"已连接真实引擎 {value.DisplayText}";
        // 同步等待持久化落盘 — Task.Run 避免 UI 线程 SynchronizationContext 死锁，Wait 阻塞至写入完成再更新 UI
        try { Task.Run(() => _session.SetVendorAsync(value.Id)).Wait(Timeout); WriteDebugLog($"SetVendorAsync ok: id={value.Id}"); }
        catch (Exception ex) { WriteErrorLog(ex); WriteDebugLog($"SetVendorAsync FAIL: {ex.Message}"); }

        RefreshModelOptions();
        OnPropertyChanged(nameof(IsMockConnection));
        // 供应商切换后 SetVendorAsync 已把 CurrentModelId 重置为新供应商默认模型，优先匹配它；找不到才取第一个
        SelectedModelOption = ModelOptions.FirstOrDefault(m => string.Equals(m.Id, _session.CurrentModelId, StringComparison.OrdinalIgnoreCase))
            ?? ModelOptions.FirstOrDefault();
        SelectedModel = SelectedModelOption?.Id;
        SelectedEffort = _session.EffortLevel.ToValue();
    }

    /// <summary>切换 Mock 引擎模式 — 独立按钮命令，按下进入 Mock 演示，再按切回真实引擎</summary>
    [RelayCommand]
    private void ToggleMock()
    {
        if (_session is PlaceholderChatSession && _realSession is not null)
        {
            _session = _realSession;
            StatusText = $"已切回真实引擎 {_session.CurrentVendor}";
        }
        else
        {
            _session = _mockSession ??= new Hosting.PlaceholderChatSession(_configService);
            _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;
            StatusText = $"已切换到 Mock 引擎（演示），模型 {_session.CurrentModelId}";
        }
        RefreshModelOptions();
        OnPropertyChanged(nameof(IsMockConnection));
        SelectedModelOption = ModelOptions.FirstOrDefault(m => string.Equals(m.Id, _session.CurrentModelId, StringComparison.OrdinalIgnoreCase))
            ?? ModelOptions.FirstOrDefault();
        SelectedModel = SelectedModelOption?.Id;
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

    /// <summary>是否可停止当前生成（生成中且未被取消）</summary>
    public bool CanStop => _sendCts is not null && !_sendCts.IsCancellationRequested;

    [RelayCommand]
    private async Task SendAsync()
    {
        var message = InputText;
        if (string.IsNullOrWhiteSpace(message) || IsBusy)
            return;

        if (_inputHistory.Count == 0 || _inputHistory[^1] != message)
            _inputHistory.Add(message);
        _historyIndex = -1;

        InputText = string.Empty;
        IsBusy = true;
        StatusText = "思考中…";
        _sendCts = new System.Threading.CancellationTokenSource();
        OnPropertyChanged(nameof(CanStop));
        try
        {
            // 应用编辑后的系统提示词（对齐 CLI --system-prompt：经 IChatService.SetSystemPromptAsync）
            if (!string.IsNullOrWhiteSpace(SystemPrompt))
            {
                await _session.SetSystemPromptAsync(SystemPrompt, _sendCts.Token);
            }

            Messages.Add(new ChatUiMessage
            {
                Role = MessageRole.User,
                Content = message,
                Timestamp = DateTime.Now
            });
            RenameActiveSessionTo(message);

            var assistant = new ChatUiMessage
            {
                Role = MessageRole.Assistant,
                Content = string.Empty,
                Timestamp = DateTime.Now,
                IsStreaming = true
            };

            var builder = new StringBuilder();
            var thinkingBuilder = new StringBuilder();
            ChatUiMessage? currentThinking = null;
            ChatUiMessage? currentToolCall = null;
            await foreach (var evt in _session.StreamAsync(message, _sendCts.Token))
            {
                switch (evt.Type)
                {
                    case ChatStreamEventType.Content:
                        if (evt.Content is not null)
                        {
                            builder.Append(evt.Content);
                            assistant.Content = builder.ToString();
                        }
                        break;
                    case ChatStreamEventType.Thinking:
                        thinkingBuilder.Append(evt.ThinkingContent);
                        if (currentThinking is null)
                        {
                            currentThinking = new ChatUiMessage
                            {
                                Role = MessageRole.Assistant,
                                Content = string.Empty,
                                Timestamp = DateTime.Now,
                                Kind = ChatUiMessageKind.Thinking
                            };
                            Messages.Add(currentThinking);
                        }
                        currentThinking.Content = thinkingBuilder.ToString();
                        break;
                    case ChatStreamEventType.ToolCallStart:
                        currentToolCall = new ChatUiMessage
                        {
                            Role = MessageRole.Assistant,
                            Content = string.Empty,
                            Timestamp = DateTime.Now,
                            Kind = ChatUiMessageKind.ToolCall,
                            ToolName = evt.ToolName,
                            ToolArguments = evt.ToolArguments,
                            ToolStartTime = DateTime.Now,
                            IsToolRunning = true
                        };
                        currentToolCall.RefreshElapsed();
                        Messages.Add(currentToolCall);
                        break;
                    case ChatStreamEventType.ToolProgress:
                        if (currentToolCall is not null && evt.ProgressMessage is not null)
                        {
                            currentToolCall.Content = evt.ProgressMessage;
                        }
                        break;
                    case ChatStreamEventType.ToolCallEnd:
                        if (currentToolCall is not null)
                        {
                            currentToolCall.IsToolRunning = false;
                            currentToolCall.RefreshElapsed();
                        }
                        Messages.Add(new ChatUiMessage
                        {
                            Role = MessageRole.Assistant,
                            Content = string.Empty,
                            Timestamp = DateTime.Now,
                            Kind = ChatUiMessageKind.ToolResult,
                            ToolName = evt.ToolName,
                            ToolResultText = evt.ToolResultText,
                            IsToolError = evt.IsToolError,
                            StructuredPatch = evt.StructuredPatch
                        });
                        currentToolCall = null;
                        break;
                }
            }

            if (currentThinking is not null)
            {
                if (string.IsNullOrWhiteSpace(currentThinking.Content))
                    Messages.Remove(currentThinking);
            }

            assistant.Content = builder.ToString();
            assistant.IsStreaming = false;
            Messages.Add(assistant);
            StatusText = "就绪";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已停止生成";
            foreach (var m in Messages)
            {
                if (m.IsStreaming)
                    m.IsStreaming = false;
            }
        }
        catch (Exception ex)
        {
            ErrorToastText = ex.Message;
            StatusText = "就绪";
            WriteErrorLog(ex);
        }
        finally
        {
            _sendCts.Dispose();
            _sendCts = null;
            IsBusy = false;
            OnPropertyChanged(nameof(CanStop));
            SaveActiveSession();
        }
    }

    /// <summary>把发送异常写入 dumps/send_error.log 以便诊断；写入失败则忽略</summary>
    private static void WriteErrorLog(Exception ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "dumps");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "send_error.log"),
                $"[{DateTime.Now:HH:mm:ss}] {ex}{Environment.NewLine}");
        }
        catch (Exception writeEx)
        {
            System.Console.Error.WriteLine($"无法写入错误日志: {writeEx.Message}");
        }
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

    /// <summary>停止当前生成（发送中按钮可用时）</summary>
    [RelayCommand]
    private void StopGenerating()
    {
        if (_sendCts is not null)
        {
            _sendCts.Cancel();
            OnPropertyChanged(nameof(CanStop));
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

        await _session.RewindLastTurnAsync().ConfigureAwait(false);
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
        await _session.ClearHistoryAsync().ConfigureAwait(false);
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
        await _session.LoadHistoryAsync(historyForEngine).ConfigureAwait(false);
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

    /// <summary>跳到最新消息（回底按钮命令）</summary>
    [RelayCommand]
    private void ScrollToBottom()
    {
        IsBackToBottomVisible = false;
        ScrollToBottomRequested?.Invoke();
    }
}