namespace JoinCode.Abstractions.Shell;

/// <summary>
/// Shell 工具门控服务 — 对齐 TS isPowerShellToolEnabled()
/// 非 Windows 平台禁用 PowerShell 工具，环境变量可覆盖
/// </summary>
public interface IShellToolGateService
{
    /// <summary>
    /// PowerShell 工具是否启用
    /// 对齐 TS: 非 Windows → false; 环境变量 JCC_USE_POWERSHELL_TOOL 可覆盖
    /// </summary>
    bool IsPowerShellToolEnabled();
}
