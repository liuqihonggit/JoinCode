namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// &gt; nul 重定向二次确认守卫 — ADR 0012 阶段5
/// <para>
/// 拦截重定向到 Windows 保留设备名（nul/con/prn/aux/com1-9/lpt1-9）的操作:
/// <list type="bullet">
/// <item>git bash 不识别保留设备名,会创建名为 nul/con/prn 的普通文件</item>
/// <item>检测到 &gt; &lt;保留设备名&gt; / 2&gt;&lt;保留设备名&gt; / &gt;&gt;&lt;保留设备名&gt; / &lt;&lt;保留设备名&gt; 等重定向</item>
/// <item>返回 <see cref="CommandDecision.Deny"/> 附警告消息,引导用户改用 /dev/null</item>
/// <item>不拦截文件创建操作（touch nul）— 只拦重定向操作</item>
/// </list>
/// </para>
/// <para>设备名数据源委托 <see cref="RetainedDeviceNames"/>（唯一数据源）</para>
/// </summary>
[Register(typeof(ICommandGuard), ServiceLifetime.Singleton)]
public sealed partial class NullRedirectTwoPhaseGuard : ICommandGuard
{
    /// <inheritdoc/>
    public string Name => "NullRedirectTwoPhaseGuard";

    /// <inheritdoc/>
    public int Priority => 700;

    /// <inheritdoc/>
    public bool CanHandle(string command, GuardContext context)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        return RetainedDeviceNames.FindAfterRedirect(command) is not null;
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, GuardContext context)
    {
        var deviceName = RetainedDeviceNames.FindAfterRedirect(command);

        if (deviceName is null)
            return new CommandDecision.Allow();

        return new CommandDecision.Deny(
            ToolDiagnostic.Create(
                "JCC9005",
                $"检测到重定向到 Windows 保留设备名 '{deviceName}' — 在 git bash 中会创建名为 '{deviceName}' 的文件。" +
                $"若本意是丢弃输出，请改用 > /dev/null。若确需创建名为 '{deviceName}' 的文件，请使用 touch {deviceName}。",
                "设备名", deviceName,
                "请改用 > /dev/null 丢弃输出，或使用 touch 创建文件"));
    }
}
