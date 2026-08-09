using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Gui.Hosting;
using JoinCode.Gui.Persistence;
using JoinCode.Gui.ViewModels;
using JoinCode.Gui.Views;
using Xunit;

namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// MainWindow 回归测试 — 验证 XAML 命名字段在构造函数后即完成赋值（InitializeComponent），
/// 且真实窗口上发送消息不会因自动滚动回调抛 NRE（曾因直接调用 AvaloniaXamlLoader.Load 导致字段为 null）。
/// 同时验证 Enter/Shift+Enter 的发送/换行语义。
/// </summary>
public sealed class MainWindowRegressionTests
{
    [AvaloniaFact]
    public void Constructor_AssignsXamlNamedFields()
    {
        var win = new MainWindow();
        var field = typeof(MainWindow).GetField(
            "MessageScroll",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field!.GetValue(win));
    }

    [AvaloniaFact]
    public void SendOnRealWindow_NoNre_AndCompletes()
    {
        var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "hello";
        vm.SendCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("就绪", vm.StatusText);
    }

    [AvaloniaFact]
    public void EnterKey_SendsMessage()
    {
        var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "enter-test";
        var input = win.FindControl<TextBox>("InputTextBox")!;
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Messages.Count > 0);
    }

    [AvaloniaFact]
    public void ShiftEnterKey_InsertsNewline_DoesNotSend()
    {
        var vm = new MainViewModel(null, new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"));
        var win = new MainWindow { DataContext = vm };
        win.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.InputText = "abc";
        var input = win.FindControl<TextBox>("InputTextBox")!;
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
        var vm = new MainViewModel(new ThrowingSession(), new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"));
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
        var vm = new MainViewModel(new ThrowingSession(), new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"));
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
        var vm = new MainViewModel(new ThrowingSession(), new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"));
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

    /// <summary>流式抛异常的假会话，用于真实窗口上验证错误 toast</summary>
    private sealed class ThrowingSession : IJccChatSession
    {
        public Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>>? PermissionConfirmationHandler { get; set; }

        public bool IsReady => true;
        public string CurrentProvider => "fake";
        public string CurrentModelId => "fake-model";
        public IReadOnlyList<string> AvailableModels => ["fake-model"];
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
        public EffortLevel EffortLevel => EffortLevel.Auto;
        public Task SetEffortLevelAsync(EffortLevel effortLevel, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public float? Temperature => null;
        public int? MaxTokens => null;
        public Task SetTemperatureAsync(float temperature, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetMaxTokensAsync(int maxTokens, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IReadOnlyList<SlashCommandMetadata> GetAvailableSlashCommands() => [];
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
