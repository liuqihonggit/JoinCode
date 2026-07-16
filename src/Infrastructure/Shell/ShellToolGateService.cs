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
    /// 非 Windows → 禁用; ant 用户默认启用(opt-out); external 用户默认禁用(opt-in)
    /// </summary>
    private static bool ComputeIsPowerShellToolEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        var env = Environment.GetEnvironmentVariable("JCC_USE_POWERSHELL_TOOL");
        var isAntUser = Environment.GetEnvironmentVariable("JCC_USER_TYPE")
            ?.Equals("ant", StringComparison.OrdinalIgnoreCase) == true;

        if (isAntUser)
        {
            // ant 用户: 默认启用，JCC_USE_POWERSHELL_TOOL=0/false 关闭
            return env is null
                || (!env.Equals("0", StringComparison.OrdinalIgnoreCase)
                    && !env.Equals("false", StringComparison.OrdinalIgnoreCase));
        }

        // external 用户: 默认禁用，JCC_USE_POWERSHELL_TOOL=1/true 启用
        return env is not null
            && (env.Equals("1", StringComparison.OrdinalIgnoreCase)
                || env.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}
