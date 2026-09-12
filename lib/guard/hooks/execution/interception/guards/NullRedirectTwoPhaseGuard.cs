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
/// </summary>
[Register(typeof(ICommandGuard), ServiceLifetime.Singleton)]
public sealed partial class NullRedirectTwoPhaseGuard : ICommandGuard
{
    /// <summary>
    /// 匹配重定向到 Windows 保留设备名 — [fd]op + 保留设备名
    /// <para>fd 可选(0/1/2/&amp;), op 为 &gt;, &gt;&gt;, &gt;|, &amp;&gt;, &lt; 等</para>
    /// <para>保留设备名: nul, con, prn, aux, com1-9, lpt1-9</para>
    /// </summary>
    [GeneratedRegex(@"(?<fd>\d*[<>]|\&)?(?<op>>\>?\|?|<)\s*(nul|con|prn|aux|com[1-9]|lpt[1-9])\b", RegexOptions.IgnoreCase)]
    private static partial Regex RetainedDeviceRedirectRegex();

    /// <inheritdoc/>
    public string Name => "NullRedirectTwoPhaseGuard";

    /// <inheritdoc/>
    public int Priority => 700;

    /// <inheritdoc/>
    public bool CanHandle(string command, GuardContext context)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        return RetainedDeviceRedirectRegex().IsMatch(command);
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, GuardContext context)
    {
        if (!RetainedDeviceRedirectRegex().IsMatch(command))
            return new CommandDecision.Allow();

        var match = RetainedDeviceRedirectRegex().Match(command);
        var deviceName = match.Groups[3].Value;

        return new CommandDecision.Deny(
            ToolDiagnostic.Create(
                "JCC9005",
                $"检测到重定向到 Windows 保留设备名 '{deviceName}' — 在 git bash 中会创建名为 '{deviceName}' 的文件。" +
                "若本意是丢弃输出，请改用 > /dev/null。若确需创建名为 '{deviceName}' 的文件，请使用 touch {deviceName}。",
                "设备名", deviceName,
                "请改用 > /dev/null 丢弃输出，或使用 touch 创建文件"));
    }
}
