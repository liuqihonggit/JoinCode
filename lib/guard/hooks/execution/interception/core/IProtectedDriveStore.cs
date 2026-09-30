namespace Core.Hooks.Execution.Interception;

/// <summary>
/// 受保护盘号存储 — 存储用户在 GUI 🛡 面板勾选的保护盘号（如 "C:" "D:"）。
/// <para>
/// 供 <see cref="Guards.DriveProtectionGuard"/> 读取，拦截涉及保护盘根目录的扫盘/删盘命令。
/// volatile 双变量原子切换，无锁读取安全（对齐 IToolHealthMonitor.UpdateBlacklist 模式）。
/// </para>
/// <para>ADR 0123 — 磁盘根保护 GUI 暴露与引擎解耦</para>
/// </summary>
public interface IProtectedDriveStore {
    /// <summary>当前受保护盘号集合（不可变快照，无锁读取）</summary>
    FrozenSet<string> ProtectedDrives { get; }

    /// <summary>更新保护盘号集合（原子替换，立即生效）</summary>
    /// <param name="drives">盘号集合（如 {"C:", "D:"}），空集合表示无保护</param>
    void Update(FrozenSet<string> drives);
}
