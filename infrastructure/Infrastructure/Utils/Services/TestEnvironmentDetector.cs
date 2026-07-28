
namespace Core.Utils;

/// <summary>
/// AOT-safe test environment detection utility.
/// Replaces AppDomain.CurrentDomain.GetAssemblies() which is not NativeAOT-compatible.
/// </summary>
public static class TestEnvironmentDetector
{
    private static readonly Lazy<bool> _cachedResult = new(Detect);

    /// <summary>
    /// Gets whether the current process is running in a test environment.
    /// Result is cached after first evaluation for performance.
    /// </summary>
    public static bool IsTestEnvironment => _cachedResult.Value;

    /// <summary>
    /// Gets whether the current process is in non-interactive mode (input redirected or test environment).
    /// Combines Console.IsInputRedirected and IsTestEnvironment into a single check.
    /// Use this as the standard guard for all interactive input operations (ReadLine/ReadKey/Read).
    /// </summary>
    public static bool IsNonInteractive => System.Console.IsInputRedirected || IsTestEnvironment;

    private static bool Detect()
    {
        // 1. Environment variable: DOTNET_ENVIRONMENT=Development (standard .NET convention)
        if (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") is "Development")
            return true;

        // 2. Environment variable: JCC_FORCE_TERMINAL is set (project-specific test indicator)
        if (Environment.GetEnvironmentVariable(JccEnvVar.ForceTerminal.ToValue()) is not null)
            return true;

        // 3. Entry assembly name contains "testhost" (AOT-safe: only checks entry assembly, no reflection)
        var entryAssemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        if (entryAssemblyName is not null &&
            entryAssemblyName.Contains("testhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // 4. Command line args contain test-related keywords
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            if (arg.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                arg.Contains("testhost", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
