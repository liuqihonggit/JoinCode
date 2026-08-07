using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using JoinCode.Gui.Theming;
using JoinCode.Gui.ViewModels;
using JoinCode.Gui.Views;

namespace JoinCode.Gui;

/// <summary>
/// Avalonia 应用入口定义。
/// </summary>
public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        GuiAppResources.Register(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception ?? new Exception("未知异常"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = CreateViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>组装真实的引擎会话并注入 ViewModel，失败时回退到占位会话以保证 UI 可启动</summary>
    private static MainViewModel CreateViewModel()
    {
        try
        {
            var session = Hosting.JccChatSession.CreateAsync().GetAwaiter().GetResult();
            return new MainViewModel(session);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            return new MainViewModel();
        }
    }

    /// <summary>将未处理异常写入日志（GUI 进程无控制台，崩溃时便于诊断）</summary>
    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "dumps");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log"),
                $"{DateTime.Now:O}\n{ex}");
        }
        catch (Exception logEx)
        {
            // 崩溃日志文件写入失败时，退回标准错误流，保证崩溃原因不被完全吞掉
            Console.Error.WriteLine($"[crash] {ex} | [log-fail] {logEx}");
        }
    }
}