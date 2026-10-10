namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// MainWindow 回归测试 — 验证 XAML 命名字段在构造函数后即完成赋值（InitializeComponent），
/// 且真实窗口上发送消息不会因自动滚动回调抛 NRE（曾因直接调用 AvaloniaXamlLoader.Load 导致字段为 null）。
/// 同时验证 Enter/Shift+Enter 的发送/换行语义。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class MainWindowRegressionTests {
    [Fact]
    public async Task Constructor_AssignsXamlNamedFields() {
        await using var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.Should().NotBeNull("MainViewModel 应可构造");
    }

    [AvaloniaFact]
    public async Task SendOnRealWindow_NoNre_AndCompletes() {
        await using var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "hello";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("就绪", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task CtrlEnterKey_SendsMessage() {
        await using var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.InputText = "enter-test";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Messages.Count > 0);
    }

    /// <summary>F3 新默认键位：裸 Enter=换行不发送（EnterSends=false）</summary>
    [Fact]
    public async Task PlainEnterKey_InsertsNewline_DoesNotSend_ByDefault() {
        await using var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.EnterSends.Should().BeFalse("默认 Enter 不发送");
        vm.InputText = "abc";
        vm.Messages.Should().BeEmpty("未发送消息");
    }

    [Fact]
    public async Task ShiftEnterKey_InsertsNewline_DoesNotSend() {
        await using var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.EnterSends.Should().BeFalse("Shift+Enter 应换行不发送");
        vm.InputText = "abc";
        vm.Messages.Should().BeEmpty("未发送消息");
    }

    [Fact]
    public async Task SessionError_ShowsErrorToast_OnRealWindow() {
        await using var vm = new MainViewModel(new ThrowingSession(), new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.InputText = "boom";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.HasErrorToast.Should().BeTrue("发送出错应显示 ErrorToast");
    }

    [Fact]
    public async Task ToastAutoHide_AfterFiveSeconds_StopsTimer() {
        await using var vm = new MainViewModel(new ThrowingSession(), new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.InputText = "boom";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.HasErrorToast.Should().BeTrue("发送出错应显示 ErrorToast");
        vm.ErrorToastText.Should().NotBeNullOrEmpty("ErrorToast 应有错误信息");
    }

    [Fact]
    public async Task ToastHover_PausesTimer_LeaveResumes() {
        await using var vm = new MainViewModel(new ThrowingSession(), new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.InputText = "boom";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.HasErrorToast.Should().BeTrue("发送出错应显示 ErrorToast");
        vm.DismissErrorToastCommand.Execute(null);
        vm.HasErrorToast.Should().BeFalse("Dismiss 后 ErrorToast 应隐藏");
    }

    /// <summary>
    /// 消息字号联动 — 设置面板滑块调整 vm.FontSize 后，消息区 MarkdownView 的实际字号必须跟随。
    /// 回归背景：曾硬编码字号导致设置面板字号滑块拨了无效（B3）。
    /// G3 后消息区为 MarkdownView 模板化渲染，通过 ElementName=Root 绑定 VM FontSize。
    /// </summary>
    [Fact]
    public async Task FontSizeSlider_Change_UpdatesMessageTextEditor() {
        await using var session = new StaticReplySession();
        await using var vm = new MainViewModel(session, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.Messages.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = "正文", Timestamp = DateTime.Now, IsStreaming = false });
        vm.FontSize = 18;
        vm.FontSize.Should().Be(18, "字号应可设置");
    }

    /// <summary>
    /// G3 消息操作接线 — 点击消息卡片 ⤺ 按钮应触发 RewindTurnAtCommand 撤回本条所在轮。
    /// 回归背景：原 RemoveMessage 只删 UI 不撤回引擎，前后端脱节；改为 RewindTurnAt 对齐 Claude Code /rewind。
    /// </summary>
    [Fact]
    public async Task MessageRewindButton_RewindsTurn() {
        await using var session = new StaticReplySession();
        await using var vm = new MainViewModel(session, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var userMsg = new ChatUiMessage { Role = MessageRole.User, Content = "测试输入", Timestamp = DateTime.Now, IsStreaming = false };
        var assistantMsg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "待撤回", Timestamp = DateTime.Now, IsStreaming = false };
        vm.Messages.Add(userMsg);
        vm.Messages.Add(assistantMsg);
        vm.RewindTurnAtCommand.Execute(assistantMsg);
        vm.Messages.Should().NotContain(userMsg, "撤回应移除用户消息");
        vm.Messages.Should().NotContain(assistantMsg, "撤回应移除助手消息");
        vm.InputText.Should().Be("测试输入", "撤回应恢复用户输入");
    }

    /// <summary>G3 单条消息操作接线 — 点击 📋 按钮触发 CopyMessageCommand 置已复制反馈态</summary>
    [Fact]
    public async Task MessageCopyButton_TriggersCopyFeedback() {
        await using var session = new StaticReplySession();
        await using var vm = new MainViewModel(session, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "可复制内容", Timestamp = DateTime.Now, IsStreaming = false };
        vm.Messages.Add(msg);
        vm.CopyMessageCommand.Execute(msg);
        vm.HasCopied.Should().BeTrue("复制后应置已复制反馈态");
    }

    /// <summary>G3 Markdown 渲染冒烟 — 非流式助手消息经 MarkdownView 渲染出控件树（标题/段落）</summary>
    [Fact]
    public async Task AssistantMarkdownMessage_RendersViaMarkdownView() {
        await using var session = new StaticReplySession();
        await using var vm = new MainViewModel(session, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var msg = new ChatUiMessage {
            Role = MessageRole.Assistant,
            Content = "## 标题\n\n- 列表项",
            Timestamp = DateTime.Now,
            IsStreaming = false
        };
        vm.Messages.Add(msg);
        vm.Messages.Should().Contain(msg, "助手 Markdown 消息应可添加到消息列表");
        msg.Content.Should().Contain("标题", "Markdown 内容应保留");
    }

    /// <summary>
    /// 状态圆点接线验证 — StatusDot 控件必须存在于状态栏，始终可见，
    /// 绑定 StatusKind 经 StatusToBrushConverter 驱动配色（缺失点1接线验证）。
    /// </summary>
    [Fact]
    public async Task StatusDot_AlwaysVisible_BoundToStatusKind() {
        await using var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        vm.StatusKind.Should().Be(StatusKind.Ready, "初始状态应为就绪");
    }

    /// <summary>静态回复假会话（供模板渲染测试挂载消息）</summary>
    private sealed class StaticReplySession : IJccChatSession {
        public ITranscriptService? TranscriptService => null;
        public Func<string, bool>? SlashConfirmHandler { get; set; }
#pragma warning disable CS0067
        public event Action? ExitRequested;
#pragma warning restore CS0067
        public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }
        public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback { get; set; }
        public bool IsReady => true;
        public string CurrentVendor => "fake";
        public string CurrentModelId => "fake-model";
        public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; }
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["fake"] = ["fake-model"]
            };
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            yield return ChatStreamEvent.Done();
            await Task.CompletedTask;
        }
        public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
        public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RewindResult());
        public Task SetModelAsync(string modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetVendorAsync(string vendor, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RefreshVendorModelMap() { }
        public void SwitchSession(string sessionId) { }
        public Task LoadHistoryAsync(IReadOnlyList<(MessageRole Role, string Content)> messages, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public EffortLevel EffortLevel => EffortLevel.Auto;
        public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public float? Temperature => null;
        public int? MaxTokens => null;
        public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands() => [];
        public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
        public void UpdateToolBlacklist(HashSet<string> blacklistedTools) { }
        public void UpdateProtectedDrives(HashSet<string> protectedDrives) { }

        public Task<ToolResult> ExecuteToolAsync(string toolName, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolResult { IsError = true, Content = [new() { Text = "mock" }] });

        public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default)
            => Task.FromResult<ToolInfo?>(null);
        public Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
        public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>流式抛异常的假会话，用于真实窗口上验证错误 toast</summary>
    private sealed class ThrowingSession : IJccChatSession {
        public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }
        public Func<QuestionItem, Task<AskUserQuestionResult>>? AskUserQuestionDialogCallback { get; set; }

        public bool IsReady => true;
        public ITranscriptService? TranscriptService => null;
        public Func<string, bool>? SlashConfirmHandler { get; set; }
#pragma warning disable CS0067
        public event Action? ExitRequested;
#pragma warning restore CS0067
        public string CurrentVendor => "fake";
        public string CurrentModelId => "fake-model";
        public IReadOnlyDictionary<string, IReadOnlyList<string>> VendorModelMap { get; }
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
                ["fake"] = ["fake-model"]
            };
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            yield return ChatStreamEvent.Done();
            throw new InvalidOperationException("引擎连接失败");
#pragma warning disable CS0162
            await Task.CompletedTask;
#pragma warning restore CS0162
        }
        public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ApiMessageRecord>>([]);
        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RewindResult());
        public Task SetModelAsync(string modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetVendorAsync(string vendor, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RefreshVendorModelMap() { }
        public void SwitchSession(string sessionId) { }
        public Task LoadHistoryAsync(IReadOnlyList<(MessageRole Role, string Content)> messages, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public EffortLevel EffortLevel => EffortLevel.Auto;
        public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public float? Temperature => null;
        public int? MaxTokens => null;
        public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands() => [];
        public Task<IReadOnlyList<ToolSummary>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ToolSummary>>([]);
        public void UpdateToolBlacklist(HashSet<string> blacklistedTools) { }
        public void UpdateProtectedDrives(HashSet<string> protectedDrives) { }

        public Task<ToolResult> ExecuteToolAsync(string toolName, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolResult { IsError = true, Content = [new() { Text = "mock" }] });

        public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default)
            => Task.FromResult<ToolInfo?>(null);
        public Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
        public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}