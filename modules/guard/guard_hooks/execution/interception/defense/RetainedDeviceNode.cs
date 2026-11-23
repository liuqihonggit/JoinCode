namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// 保留设备名检测 node — 独立公共对象，检测重定向到 Windows 保留设备名。
/// <para>
/// 逻辑迁移自 NullRedirectTwoPhaseGuard（ADR 0012 阶段5），node 化后可被 BashDefense 链组装。
/// 设备名数据源委托 <see cref="RetainedDeviceNames"/>（唯一数据源）。
/// </para>
/// </summary>
[Register(typeof(RetainedDeviceNode), ServiceLifetime.Singleton)]
public sealed class RetainedDeviceNode {
    /// <summary>
    /// 检测命令中是否包含重定向到 Windows 保留设备名的操作。
    /// <para>
    /// 检测模式：&gt;nul / 2&gt;nul / &gt;&gt;nul / &lt;&lt;nul / &amp;&gt;nul 等重定向到保留设备名。
    /// 不检测文件创建操作（touch nul）— 只检测重定向操作。
    /// </para>
    /// </summary>
    /// <param name="command">待检测命令</param>
    /// <returns>检测到的设备名（如 "nul"/"con"/"prn" 等）；未检测到返回 null</returns>
    public string? FindRetainedDevice(string command)
        => RetainedDeviceNames.FindAfterRedirect(command);
}