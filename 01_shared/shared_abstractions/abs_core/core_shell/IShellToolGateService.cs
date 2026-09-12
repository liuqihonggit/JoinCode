namespace JoinCode.Abstractions.Shell;

/// <summary>
/// Shell 工具门控服务 — 平台级门控（非 Windows 禁用 PowerShell）
/// 关系: 本接口仅做平台级门控，命令级权限检查由 IPsPermissionChecker (04-guard) 负责
/// </summary>
public interface IShellToolGateService
{
    /// <summary>
    /// PowerShell 工具是否启用
    /// 对齐 TS: 非 Windows → false; 环境变量 JCC_USE_POWERSHELL_TOOL 可覆盖
    /// </summary>
    bool IsPowerShellToolEnabled();
}
