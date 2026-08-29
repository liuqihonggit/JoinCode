namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// MainWindow 回归测试 — 验证 XAML 命名字段在构造函数后即完成赋值（InitializeComponent），
/// 且真实窗口上发送消息不会因自动滚动回调抛 NRE（曾因直接调用 AvaloniaXamlLoader.Load 导致字段为 null）。
/// 同时验证 Enter/Shift+Enter 的发送/换行语义。
/// </summary>
[Collection("GuiUiSequential")]
public sealed class MainWindowRegressionTests
{
    [AvaloniaFact]
    public void Constructor_AssignsXamlNamedFields()
    {
        var win = new MainWindow();
        var field = typeof(MainWindow).GetField(
            "MessageScrollViewer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field!.GetValue(win));
    }

    [AvaloniaFact]
    public void SendOnRealWindow_NoNre_AndCompletes()
    {
        var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "hello";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("就绪", vm.StatusText);
    }

    [AvaloniaFact]
    public void CtrlEnterKey_SendsMessage()
    {
        var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "enter-test";
        var input = win.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "InputTextBox");
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Messages.Count > 0);
    }

    /// <summary>F3 新默认键位：裸 Enter=换行不发送（EnterSends=false）</summary>
    [AvaloniaFact]
    public void PlainEnterKey_InsertsNewline_DoesNotSend_ByDefault()
    {
        var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "abc";
        var input = win.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "InputTextBox");
        input.CaretIndex = 1;
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.InputText.Contains('\n'), "裸 Enter 应插入换行");
        Assert.Empty(vm.Messages);
    }

    [AvaloniaFact]
    public void ShiftEnterKey_InsertsNewline_DoesNotSend()
    {
        var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "abc";
        var input = win.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "InputTextBox");
        input.CaretIndex = 1;
        input.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            KeyModifiers = KeyModifiers.Shift
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("a\nbc", vm.InputText);
        Assert.Empty(vm.Messages);
    }

    [AvaloniaFact]
    public void SessionError_ShowsErrorToast_OnRealWindow()
    {
        var vm = new MainViewModel(new ThrowingSession(), new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "boom";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var toast = win.FindControl<Border>("ErrorToast")!;
        Assert.True(vm.HasErrorToast);
        Assert.True(toast.IsVisible);
    }

    [AvaloniaFact]
    public void ToastAutoHide_AfterFiveSeconds_StopsTimer()
    {
        var vm = new MainViewModel(new ThrowingSession(), new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "boom";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var toast = win.FindControl<Border>("ErrorToast")!;
        Assert.True(toast.IsVisible);

        var timer = typeof(MainWindow).GetField(
            "_errorToastTimer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(win)!;
        var isEnabled = timer.GetType().GetProperty("IsEnabled")!;
        Assert.True((bool)isEnabled.GetValue(timer)!);

        // 模拟 50 个 100ms tick = 5s 到期 → 计时器停止并开始淡出
        var tickMethod = typeof(MainWindow).GetMethod(
            "OnErrorToastTimerTick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        for (var i = 0; i < 50; i++)
        {
            tickMethod.Invoke(win, new object?[] { timer, EventArgs.Empty });
        }

        Assert.False((bool)isEnabled.GetValue(timer)!);
    }

    [AvaloniaFact]
    public void ToastHover_PausesTimer_LeaveResumes()
    {
        var vm = new MainViewModel(new ThrowingSession(), new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "boom";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var toast = win.FindControl<Border>("ErrorToast")!;
        var enter = typeof(MainWindow).GetMethod(
            "OnErrorToastPointerEnter",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var leave = typeof(MainWindow).GetMethod(
            "OnErrorToastPointerLeave",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var timer = typeof(MainWindow).GetField(
            "_errorToastTimer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(win)!;
        var isEnabled = timer.GetType().GetProperty("IsEnabled")!;

        enter.Invoke(win, new object?[] { toast, null });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False((bool)isEnabled.GetValue(timer)!);

        leave.Invoke(win, new object?[] { toast, null });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True((bool)isEnabled.GetValue(timer)!);
    }

    /// <summary>
    /// 消息字号联动 — 设置面板滑块调整 vm.FontSize 后，消息区 MarkdownView 的实际字号必须跟随。
    /// 回归背景：曾硬编码字号导致设置面板字号滑块拨了无效（B3）。
    /// G3 后消息区为 MarkdownView 模板化渲染，通过 ElementName=Root 绑定 VM FontSize。
    /// </summary>
    [AvaloniaFact]
    public void FontSizeSlider_Change_UpdatesMessageTextEditor()
    {
        var session = new StaticReplySession();
        var vm = new MainViewModel(session, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 添加一条已完成（非流式）的助手消息 → 模板生成 MarkdownView
        vm.Messages.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = "正文", Timestamp = DateTime.Now, IsStreaming = false });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var markdownView = win.GetVisualDescendants().OfType<JoinCode.Gui.Markdown.MarkdownView>().First();
        Assert.Equal(vm.FontSize, markdownView.BaseFontSize);

        vm.FontSize = 18;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(18, markdownView.BaseFontSize);
    }

    /// <summary>
    /// G3 单条消息操作接线 — 点击消息卡片 ✕ 按钮应触发 RemoveMessageCommand 从列表移除该条。
    /// 回归背景：OnRemoveClick 曾无 XAML 引用（死代码），消息删除用户不可达。
    /// </summary>
    [AvaloniaFact]
    public void MessageRemoveButton_RemovesMessage()
    {
        var session = new StaticReplySession();
        var vm = new MainViewModel(session, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var target = new ChatUiMessage { Role = MessageRole.Assistant, Content = "待删除", Timestamp = DateTime.Now, IsStreaming = false };
        vm.Messages.Add(target);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 定位模板中 CommandParameter 绑定到该消息的 ✕ 按钮（按 Tooltip 区分复制/删除）
        var removeButton = win.GetVisualDescendants()
            .OfType<Button>()
            .First(b => Avalonia.Controls.ToolTip.GetTip(b) is string tip && tip.Contains("删除") && ReferenceEquals(b.CommandParameter, target));
        removeButton.Command!.Execute(removeButton.CommandParameter);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.Messages.Should().NotContain(target);
    }

    /// <summary>G3 单条消息操作接线 — 点击 📋 按钮触发 CopyMessageCommand 置已复制反馈态</summary>
    [AvaloniaFact]
    public void MessageCopyButton_TriggersCopyFeedback()
    {
        var session = new StaticReplySession();
        var vm = new MainViewModel(session, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var msg = new ChatUiMessage { Role = MessageRole.Assistant, Content = "可复制内容", Timestamp = DateTime.Now, IsStreaming = false };
        vm.Messages.Add(msg);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var copyButton = win.GetVisualDescendants()
            .OfType<Button>()
            .First(b => Avalonia.Controls.ToolTip.GetTip(b) is string tip && tip.Contains("复制本条") && ReferenceEquals(b.CommandParameter, msg));
        copyButton.Command!.Execute(copyButton.CommandParameter);

        vm.HasCopied.Should().BeTrue();
    }

    /// <summary>G3 Markdown 渲染冒烟 — 非流式助手消息经 MarkdownView 渲染出控件树（标题/段落）</summary>
    [AvaloniaFact]
    public void AssistantMarkdownMessage_RendersViaMarkdownView()
    {
        var session = new StaticReplySession();
        var vm = new MainViewModel(session, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"), new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.Assistant,
            Content = "## 标题\n\n- 列表项",
            Timestamp = DateTime.Now,
            IsStreaming = false
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var mdView = win.GetVisualDescendants().OfType<JoinCode.Gui.Markdown.MarkdownView>().Single();
        mdView.Children.Should().NotBeEmpty("Markdown 应解析出块级控件");
    }

    /// <summary>静态回复假会话（供模板渲染测试挂载消息）</summary>
    private sealed class StaticReplySession : IJccChatSession
    {
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
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake"] = ["fake-model"]
            };
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
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
        public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
        public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>流式抛异常的假会话，用于真实窗口上验证错误 toast</summary>
    private sealed class ThrowingSession : IJccChatSession
    {
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
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake"] = ["fake-model"]
            };
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> ExecuteSlashCommandAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
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
        public Task<JoinCode.Abstractions.UI.ThemeKind> GetThemeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(JoinCode.Abstractions.UI.ThemeKind.Auto);
        public Task SetThemeAsync(JoinCode.Abstractions.UI.ThemeKind theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event EventHandler<JoinCode.Abstractions.UI.ThemeKind>? ThemeChanged { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
