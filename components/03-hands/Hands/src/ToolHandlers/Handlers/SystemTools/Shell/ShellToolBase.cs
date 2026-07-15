namespace Tools.Handlers;

/// <summary>
/// Shell 工具基类 — 统一门控检查、进程看护、压缩标记
/// 子类只需关注业务逻辑，父类自动处理：
///   1. PowerShell 门控（非 Windows 或环境变量禁用时不可用）
///   2. 进程看护注册（睡眠唤醒后自动检测僵尸进程）
///   3. 压缩标记（微压缩时按 Shell 分类链式清理，无需硬编码枚举）
/// </summary>
public abstract class ShellToolBase
{
    private readonly IShellToolGateService? _gateService;
    private readonly IShellProcessWatchdog? _watchdog;

    protected ShellToolBase(
        IShellToolGateService? gateService = null,
        IShellProcessWatchdog? watchdog = null)
    {
        _gateService = gateService;
        _watchdog = watchdog;
    }

    /// <summary>
    /// 工具名称 — 子类必须实现
    /// </summary>
    public abstract string ToolName { get; }

    /// <summary>
    /// 是否为 PowerShell 类型工具 — 子类覆盖
    /// </summary>
    public virtual bool IsPowerShell => false;

    /// <summary>
    /// 是否可被微压缩清理 — 默认 true，所有 Shell 工具结果都可压缩
    /// </summary>
    public virtual bool IsCompactable => true;

    /// <summary>
    /// 检查 PowerShell 门控 — PowerShell 工具在非 Windows 或环境变量禁用时返回错误
    /// </summary>
    /// <param name="isPowerShellCall">当前调用是否为 PowerShell 命令</param>
    protected ToolResult? CheckGate(bool isPowerShellCall = false)
    {
        if (isPowerShellCall && _gateService is not null && !_gateService.IsPowerShellToolEnabled())
        {
            return ResultBuilder.Error()
                .WithText("PowerShell tool is not available on this platform. Set JCC_USE_POWERSHELL_TOOL=1 to enable.")
                .Build();
        }

        return null;
    }

    /// <summary>
    /// 注册进程到看护 — 进程死亡时自动触发回调
    /// </summary>
    protected void RegisterWatchdog(int processId, Action<int> onProcessDied)
    {
        _watchdog?.Register(processId, onProcessDied);
    }

    /// <summary>
    /// 取消进程看护
    /// </summary>
    protected void UnregisterWatchdog(int processId)
    {
        _watchdog?.Unregister(processId);
    }
}
