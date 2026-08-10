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
        App.LogDiag("[App] Initialize begin");
        AvaloniaXamlLoader.Load(this);
        GuiAppResources.Register(this);
        App.LogDiag("[App] Initialize end");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        App.LogDiag("[App] OnFrameworkInitializationCompleted begin");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception ?? new Exception("未知异常"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 先显示窗口（占位会话），引擎在后台渐进式组装，完成后热切换，避免启动阻塞
            desktop.MainWindow = new MainWindow
            {
                DataContext = CreateViewModel()
            };
            App.LogDiag("[App] MainWindow created");
        }

        base.OnFrameworkInitializationCompleted();
        App.LogDiag("[App] OnFrameworkInitializationCompleted end");
    }

    /// <summary>
    /// 创建 ViewModel — 立即以占位会话返回（窗口快速显示），同时后台异步组装真实引擎，
    /// 组装完成后在 UI 线程热切换。引擎失败时占位会话兜底，UI 保持可用。
    /// </summary>
    private static MainViewModel CreateViewModel()
    {
        var viewModel = new MainViewModel();
        _ = Task.Run(() => Hosting.JccChatSession.CreateAsync())
            .ContinueWith(t =>
            {
                try
                {
                    var session = t.GetAwaiter().GetResult();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => viewModel.AttachRealSession(session));
                }
                catch (Exception ex)
                {
                    WriteCrashLog(ex);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        viewModel.StatusText = $"引擎加载失败，已回退到 Mock 引擎: {ex.Message}");
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        return viewModel;
    }

    /// <summary>临时诊断日志：写入 dumps 目录，用于定位启动耗时</summary>
    internal static void LogDiag(string message)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "dumps");
            Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                Path.Combine(dir, "startup_timing.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch (Exception logEx)
        {
            Console.Error.WriteLine($"[diag] timing log failed: {logEx.Message}");
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