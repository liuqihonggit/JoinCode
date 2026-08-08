using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using JoinCode.Gui.Persistence;
using JoinCode.Gui.ViewModels;
using JoinCode.Gui.Views;
using Xunit;

namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// MainWindow 回归测试 — 验证 XAML 命名字段在构造函数后即完成赋值（InitializeComponent），
/// 且真实窗口上发送消息不会因自动滚动回调抛 NRE（曾因直接调用 AvaloniaXamlLoader.Load 导致字段为 null）。
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
}
