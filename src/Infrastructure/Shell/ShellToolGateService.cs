using System.Runtime.InteropServices;

namespace Infrastructure.Shell;

/// <summary>
/// Shell 工具门控服务 — 对齐 TS isPowerShellToolEnabled()
/// </summary>
[Register]
public sealed class ShellToolGateService : IShellToolGateService
{
    private readonly bool _cachedResult;

    public ShellToolGateService()
    {
        _cachedResult = ComputeIsPowerShellToolEnabled();
    }

    public bool IsPowerShellToolEnabled() => _cachedResult;

    private static bool ComputeIsPowerShellToolEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        var env = Environment.GetEnvironmentVariable("JCC_USE_POWERSHELL_TOOL");
        if (env is not null)
        {
            return !env.Equals("0", StringComparison.OrdinalIgnoreCase)
                && !env.Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
