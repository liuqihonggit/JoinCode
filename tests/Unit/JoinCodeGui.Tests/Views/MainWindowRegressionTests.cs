using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
}
