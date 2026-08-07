using Avalonia;

namespace JoinCode.Gui;

/// <summary>
/// Programm entry — Avalonia desktop host.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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