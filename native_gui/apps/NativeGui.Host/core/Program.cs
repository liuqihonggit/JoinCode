namespace JoinCode.Gui;

/// <summary>
/// Programm entry — Avalonia desktop host.
/// </summary>
internal static class Program {
    /// <summary>程序主入口 — 启动 Avalonia 桌面生命周期；--dsh-smoke 无窗口验证 DSH 宿主。</summary>
    [STAThread]
    public static int Main(string[] args) {
        if (args.Contains("--dsh-smoke"))
            return WebHost.DshSmokeRunner.RunAsync().GetAwaiter().GetResult();  // style-allow: sync-block — GUI 宿主进程入口: Main 同步等待 WebHost 生命周期

        App.LogDiag($"[Main] entry");
        var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        App.LogDiag($"[Main] exit");
        return exitCode;
    }

    /// <summary>
    /// Avalonia app builder — keep single instance for testing reuse.
    /// </summary>
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}