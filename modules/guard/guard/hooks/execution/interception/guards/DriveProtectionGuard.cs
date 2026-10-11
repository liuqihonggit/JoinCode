namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// 磁盘根保护守卫 — 拦截涉及受保护盘根目录的扫盘/删盘命令（ADR 0123）。
/// <para>
/// 保护盘号由 GUI 🛡 面板勾选驱动，经 <see cref="IProtectedDriveStore"/> 传入。
/// 命令参数为盘根路径（如 C:\ D:\）且该盘在保护列表中 → <see cref="CommandDecision.Deny"/>。
/// 子目录操作（如 C:\Users\foo）不受影响（<see cref="PathSafetyValidator.IsRootPath"/> 判断）。
/// </para>
/// <para>
/// 与 <c>DangerousCommandProtectionMiddleware</c> 的关系：
/// DangerousCommandProtectionMiddleware 是硬编码安全红线（rm -rf / 即使 Bypass 也拒绝），
/// 本守卫是用户可控加层（可取消勾选放行特定盘），双层保护。
/// </para>
/// </summary>
[Register(typeof(ICommandGuard), ServiceLifetime.Singleton)]
public sealed class DriveProtectionGuard : ICommandGuard {
    private readonly IProtectedDriveStore _store;

    /// <summary>初始化 DriveProtectionGuard 实例</summary>
    public DriveProtectionGuard(IProtectedDriveStore store) => _store = store;

    /// <inheritdoc/>
    public string Name => "DriveProtectionGuard";

    /// <inheritdoc/>
    public int Priority => 900;

    /// <inheritdoc/>
    public bool CanHandle(string command, GuardContext context) {
        if (string.IsNullOrWhiteSpace(command))
            return false;
        var drives = _store.ProtectedDrives;
        if (drives.Count == 0)
            return false;
        return FindProtectedDriveRootInCommand(command, drives) is not null;
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, GuardContext context) {
        var drives = _store.ProtectedDrives;
        var matchedDrive = FindProtectedDriveRootInCommand(command, drives);
        if (matchedDrive is null)
            return new CommandDecision.Allow();

        return new CommandDecision.Deny(ToolDiagnostic.Create(
            reason: "DriveRootProtected",
            formattedMessage: $"磁盘根保护：命令涉及受保护盘 {matchedDrive} 的根目录操作，已被拦截。\n" +
                              $"触发守卫：DriveProtectionGuard（Priority=900）\n" +
                              $"如需放行：请在 GUI 🛡 面板取消 {matchedDrive} 盘号的勾选。",
            detailKey: "ProtectedDrive",
            detailValue: matchedDrive,
            suggestions: $"在 GUI 🛡 AI 工具拦截器面板取消 {matchedDrive} 盘号的勾选"
        ));
    }

    /// <summary>
    /// 检查命令参数是否包含保护盘的根路径 — 供测试直接调用
    /// </summary>
    internal static string? FindProtectedDriveRootInCommand(string command, FrozenSet<string> drives) {
        var shellCmd = ShellCommand.Parse(command);
        foreach (var arg in shellCmd.Arguments) {
            if (!PathSafetyValidator.IsRootPath(arg))
                continue;
            var driveLetter = ExtractDriveLetter(arg);
            if (driveLetter is not null && drives.Contains(driveLetter))
                return driveLetter;
        }
        return null;
    }

    /// <summary>从路径提取盘号（如 "C:\" → "C:"），非盘根返回 null</summary>
    private static string? ExtractDriveLetter(string path) {
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            return char.ToUpperInvariant(path[0]).ToString() + ":";
        return null;
    }
}
