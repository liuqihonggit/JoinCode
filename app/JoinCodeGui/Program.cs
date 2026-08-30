namespace JoinCode.Gui;

/// <summary>
/// Programm entry — Avalonia desktop host.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.LogDiag($"[Main] entry");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        App.LogDiag($"[Main] exit");
    }

    /// <summary>
    /// Avalonia app builder — keep single instance for testing reuse.
    /// </summary>
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}