namespace JoinCode.Gui;

/// <summary>
/// Programm entry — Avalonia desktop host.
/// </summary>
internal static class Program {
    /// <summary>程序主入口 — 启动 Avalonia 桌面生命周期；--dsh-smoke 无窗口验证 DSH 宿主。</summary>
    [STAThread]
    public static async Task<int> Main(string[] args) {
        if (args.Contains("--dsh-smoke"))
            return await WebHost.DshSmokeRunner.RunAsync();

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