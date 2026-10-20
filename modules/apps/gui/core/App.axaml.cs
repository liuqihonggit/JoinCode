namespace JoinCode.Gui;

/// <summary>
/// Avalonia 应用入口定义。
/// </summary>
public sealed partial class App : Application {
    /// <summary>初始化应用</summary>
    public override void Initialize() {
        App.LogDiag("[App] Initialize begin");
        AvaloniaXamlLoader.Load(this);
        GuiAppResources.Register(this);
        App.LogDiag("[App] Initialize end");
    }

    /// <summary>框架初始化完成时处理</summary>
    public override void OnFrameworkInitializationCompleted() {
        App.LogDiag("[App] OnFrameworkInitializationCompleted begin");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception ?? new Exception("未知"));
        TaskScheduler.UnobservedTaskException += (_, e) => {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--webui-smoke")) {
                // 自研 WebUI 冒烟（ADR 0138 遗留对照路径，file:// + chrome.webview 桥）
                var pagePath = Path.Combine(AppContext.BaseDirectory, "webui", "index.html");
                desktop.MainWindow = new WebUiWindow(new Uri(pagePath).AbsoluteUri, ResolveWebViewProfile(), smoke: true);
                App.LogDiag("[App] WebUiWindow(smoke) created");
            } else {
                // DSH 前端整体托管（ADR 0143）— 默认路径：回环 HTTP/WS 宿主 + WebView2
                desktop.MainWindow = new DshWebUiWindow(ResolveWebViewProfile());
                App.LogDiag("[App] DshWebUiWindow created");
            }
        }

        base.OnFrameworkInitializationCompleted();
        App.LogDiag("[App] OnFrameworkInitializationCompleted end");
    }

    /// <summary>
    /// WebView2 用户数据目录：默认 <c>~/.jcc/webview2</c>；
    /// 环境变量 <c>JCC_WEBVIEW_DATA_DIR</c> 可覆盖（E2E 用干净配置目录，避免上次运行的 localStorage/IndexedDB 状态干扰断言）。
    /// </summary>
    private static string ResolveWebViewProfile() =>
        Environment.GetEnvironmentVariable("JCC_WEBVIEW_DATA_DIR") is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(AppDataConstants.Paths.JccDirectory, "webview2");

    /// <summary>临时诊断日志：写入 dumps 目录，用于定位启动耗时</summary>
    internal static void LogDiag(string message) => _ = LogDiagAsync(message);

    private static async Task LogDiagAsync(string message) {
        try {
            var dir = AppDataConstants.Paths.DumpsDirectory;
            Directory.CreateDirectory(dir);
            await SafeFileIO.AppendAllText(
                Path.Combine(dir, "startup_timing.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        } catch (Exception logEx) {
            Console.Error.WriteLine($"[diag] timing log failed: {logEx.Message}");
        }
    }

    /// <summary>将未处理异常写入日志（GUI 进程无控制台，崩溃时便于诊断）</summary>
    private static void WriteCrashLog(Exception ex) => _ = WriteCrashLogAsync(ex);

    private static async Task WriteCrashLogAsync(Exception ex) {
        try {
            var dir = AppDataConstants.Paths.DumpsDirectory;
            Directory.CreateDirectory(dir);
            await SafeFileIO.WriteAllText(
                Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log"),
                $"{DateTime.Now:O}\n{ex}");
        } catch (Exception logEx) {
            // 崩溃日志文件写入失败时，退回标准错误流，保证崩溃原因不被完全吞掉
            Console.Error.WriteLine($"[crash] {ex} | [log-fail] {logEx}");
        }
    }
}
