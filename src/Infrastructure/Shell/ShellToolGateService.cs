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

    /// <summary>
    /// 计算 PowerShell 工具是否启用 — 对齐 TS isPowerShellToolEnabled()
    /// 非 Windows → 禁用; ant 用户默认启用; external 用户默认禁用
    /// </summary>
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

        // 对齐 TS: ant 用户默认 true，external 用户默认 false
        var userType = Environment.GetEnvironmentVariable("JCC_USER_TYPE");
        return userType?.Equals("ant", StringComparison.OrdinalIgnoreCase) == true;
    }
}
