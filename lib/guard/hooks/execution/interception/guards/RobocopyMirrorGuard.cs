namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// robocopy /MIR 和 /PURGE 保留名清理场景白名单放行守卫 — ADR 0012 阶段3
/// <para>
/// 放行条件（全部满足才放行）:
/// <list type="number">
/// <item>命令是 robocopy 且含 /MIR 或 /PURGE</item>
/// <item>目标路径含 Windows 保留名文件（nul/con/prn/aux/com1-9/lpt1-9）</item>
/// </list>
/// 满足 → <see cref="CommandDecision.Allow"/>（放行，权限中间件降级为红灯确认）
/// 不满足 → <see cref="CommandDecision.Handoff"/>（交给下游，权限中间件按黑灯拒绝）
/// </para>
/// <para>设备名数据源委托 <see cref="RetainedDeviceNames"/>（唯一数据源）</para>
/// </summary>
[Register(typeof(ICommandGuard), ServiceLifetime.Singleton)]
public sealed class RobocopyMirrorGuard : ICommandGuard {
    /// <inheritdoc/>
    public string Name => "RobocopyMirrorGuard";

    /// <inheritdoc/>
    public int Priority => 500;

    /// <inheritdoc/>
    public bool CanHandle(string command, GuardContext context) {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var lower = command.AsSpan();
        if (!lower.StartsWith("robocopy", StringComparison.OrdinalIgnoreCase))
            return false;

        return lower.Contains("/mir", StringComparison.OrdinalIgnoreCase) ||
               lower.Contains("/purge", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, GuardContext context) {
        var shellCmd = ShellCommand.Parse(command);

        // robocopy 参数顺序: robocopy <source> <destination> [options]
        // 目标路径是第二个参数
        if (shellCmd.Arguments.Count < 2)
            return new CommandDecision.Handoff();

        var targetPath = shellCmd.Arguments[1];

        return IsRetainedNameCleanupScenario(targetPath)
            ? new CommandDecision.Allow()
            : new CommandDecision.Handoff();
    }

    /// <summary>
    /// 检查目标路径是否属于保留名清理场景 — 委托 <see cref="RetainedDeviceNames.FindInPath"/>
    /// </summary>
    /// <param name="targetPath">robocopy 目标路径</param>
    /// <returns>true 表示路径含保留名文件（保留名清理场景）</returns>
    public static bool IsRetainedNameCleanupScenario(string targetPath)
        => RetainedDeviceNames.FindInPath(targetPath);

    /// <summary>
    /// 检查完整命令是否属于 robocopy /MIR 保留名清理场景 — 供 DangerousCommandProtectionMiddleware 降级使用
    /// </summary>
    /// <param name="command">完整命令字符串</param>
    /// <returns>true 表示命令是 robocopy /MIR/PURGE 且目标路径含保留名文件</returns>
    public static bool IsRobocopyMirrorRetainedNameCleanup(string command) {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var lower = command.AsSpan();
        if (!lower.StartsWith("robocopy", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!lower.Contains("/mir", StringComparison.OrdinalIgnoreCase) &&
            !lower.Contains("/purge", StringComparison.OrdinalIgnoreCase))
            return false;

        var shellCmd = ShellCommand.Parse(command);
        if (shellCmd.Arguments.Count < 2)
            return false;

        return IsRetainedNameCleanupScenario(shellCmd.Arguments[1]);
    }
}