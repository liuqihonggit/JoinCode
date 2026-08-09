using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JoinCode.Abstractions.Configuration.Llm;
using JoinCode.Abstractions.Configuration.Providers;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Gui.Hosting;

namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 主窗口 ViewModel — 承载引擎会话门面与基础对话占位。
/// 依赖注入仅走 <see cref="IJccChatSession"/>，不触碰引擎内部实现。
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly IJccChatSession? _realSession;
    private IJccChatSession? _mockSession;
    private IJccChatSession _session;
    private readonly Persistence.GuiSessionStore _sessionStore;

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
    private string _selectedModel;

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
        Task.Run(async () =>
        {
            try
            {
                await _session.SetEffortLevelAsync(effort).WaitAsync(Timeout);
            }
            catch (Exception ex)
            {
                StatusText = $"设置推理力度失败: {ex.Message}";
            }
        });
    }

    /// <summary>设置面板是否展开</summary>
    [ObservableProperty]
    private bool _isSettingsPanelOpen;

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
        ? Messages.Where(m => (m.Content ?? string.Empty).Contains(SearchText, StringComparison.OrdinalIgnoreCase))
        : Messages;

    partial void OnSearchTextChanged(string value)
        => OnPropertyChanged(nameof(FilteredMessages));

    /// <summary>模型下拉选项（绑定引擎共享配置的真实模型列表，展示"供应商 · 模型显示名"）</summary>
    public IReadOnlyList<ModelOptionItem> ModelOptions
    {
        get
        {
            var provider = _session.CurrentProvider;
            var providerDisplay = ProviderKindExtensions.FromValue(provider)?.ToString() ?? provider;
            var catalog = ModelConfigLoader.GetModels(provider);

            var source = _session.AvailableModels;
            var items = new ModelOptionItem[source.Count];
            for (var i = 0; i < source.Count; i++)
            {
                var id = source[i];
                var entry = catalog.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                var modelDisplay = entry?.DisplayName ?? id;
                items[i] = new ModelOptionItem(id, $"{providerDisplay} · {modelDisplay}");
            }
            return items;
        }
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

    public MainViewModel(IJccChatSession? session = null, Persistence.GuiSessionStore? store = null)
    {
        _realSession = session;
        _session = session ?? new Hosting.PlaceholderChatSession();
        _sessionStore = store ?? new Persistence.GuiSessionStore(new IO.FileSystem.PhysicalFileSystem());
        _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;
        _selectedModel = _session.CurrentModelId;
        _selectedModelOption = ModelOptions.FirstOrDefault(m => m.Id == _session.CurrentModelId);
        _selectedEffort = _session.EffortLevel.ToValue();
        _selectedConnection = session is null ? MockConnection : BuildRealConnection(session);
        Messages.CollectionChanged += OnMessagesChanged;
        LoadPersistedSessions();
        NewConversation();
    }

    /// <summary>获取可用斜杠命令清单 — 委托到引擎 session，由源码生成器自动提取</summary>
    public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands()
        => _session.GetAvailableSlashCommands();

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
        OnPropertyChanged(nameof(MessageCount));
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(TotalChars));
        OnPropertyChanged(nameof(EstimatedTokens));
        OnPropertyChanged(nameof(FilteredMessages));
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
        => WriteBackTemperatureAndMaxTokens();

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

    /// <summary>用户切换模型下拉项时回写共享配置（绑定同一个配置源，下次请求引擎生效）</summary>
    partial void OnSelectedModelOptionChanged(ModelOptionItem? value)
    {
        if (value is not null && value.Id != _session.CurrentModelId)
        {
            SelectedModel = value.Id;
        }
    }

    /// <summary>用户切换模型时回写共享配置（绑定同一个配置源，下次请求引擎生效）</summary>
    partial void OnSelectedModelChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != _session.CurrentModelId)
        {
            _session.SetModelAsync(value);
        }
    }

    /// <summary>Mock 引擎连接候选（始终存在于下拉列表，用于演示/本地验证）</summary>
    private static readonly ConnectionOptionItem MockConnection = new()
    {
        Id = "mock",
        DisplayText = "🧪 Mock 引擎（演示）",
        IsMock = true
    };

    /// <summary>真实供应商连接候选（由注入会话的 CurrentProvider 派生展示文本）</summary>
    private static ConnectionOptionItem BuildRealConnection(IJccChatSession session)
    {
        var provider = session.CurrentProvider;
        var display = ProviderKindExtensions.FromValue(provider)?.ToString() ?? provider;
        return new ConnectionOptionItem
        {
            Id = provider,
            DisplayText = $"{display}（真实）",
            IsMock = false
        };
    }

    /// <summary>连接下拉候选（Mock 引擎 + 真实供应商；无真实会话时仅展示 Mock）</summary>
    public IReadOnlyList<ConnectionOptionItem> ConnectionOptions
    {
        get
        {
            var options = new List<ConnectionOptionItem> { MockConnection };
            if (_realSession is not null)
                options.Add(BuildRealConnection(_realSession));
            return options;
        }
    }

    /// <summary>当前选中的连接项（切换时替换活动会话，不销毁任何会话）</summary>
    [ObservableProperty]
    private ConnectionOptionItem? _selectedConnection;

    /// <summary>当前是否连接 Mock 引擎（驱动状态提示与 Mock 徽标显隐）</summary>
    public bool IsMockConnection => _session is PlaceholderChatSession;

    partial void OnSelectedConnectionChanged(ConnectionOptionItem? value)
    {
        if (value is null)
            return;

        var wantMock = value.IsMock;
        var isMock = _session is PlaceholderChatSession;
        if (wantMock == isMock)
            return;

        if (wantMock)
        {
            _session = _mockSession ??= new Hosting.PlaceholderChatSession();
            _session.PermissionConfirmationHandler = OnPermissionConfirmationRequestedAsync;
            StatusText = $"已切换到 Mock 引擎（演示），模型 {_session.CurrentModelId}";
        }
        else if (_realSession is not null)
        {
            _session = _realSession;
            StatusText = $"已连接真实引擎 {value.DisplayText}";
        }
        else
        {
            return;
        }

        OnPropertyChanged(nameof(ModelOptions));
        OnPropertyChanged(nameof(IsMockConnection));
        SelectedModel = _session.CurrentModelId;
        SelectedModelOption = ModelOptions.FirstOrDefault(m => m.Id == _session.CurrentModelId);
        SelectedEffort = _session.EffortLevel.ToValue();
    }

    /// <summary>输入框变化时同步字符计数，并退出历史回看游标</summary>
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

    /// <summary>是否有可重新生成的上一轮回复</summary>
    public bool CanRegenerate => Messages.Any(m => m.Role == MessageRole.Assistant);

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
    private void SelectSession(SessionItem? session)
    {
        if (session is null)
            return;
        if (session == _activeSession)
            return;

        foreach (var s in Sessions)
            s.IsSelected = s == session;
        _activeSession = session;

        // 切换会话时从持久化恢复该会话消息到消息区（空会话则清空）
        var data = _sessionStore.Load(session.Id);
        Messages.Clear();
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
            }
        }
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
}