namespace Core.Security.DangerClassification;

/// <summary>
/// 命令映射表构建 — 所有危险命令的统一定义
/// </summary>
public static partial class DangerousCommandCatalog
{
    private static FrozenDictionary<string, CommandEntry> BuildCommands()
    {
        var entries = new Dictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase)
        {
            // === Forbidden（绝对禁止 AI 执行）— 整盘/系统级不可逆操作 ===
            ["mkfs"] = new("mkfs", CommandRisk.SystemModification, CommandDangerLevel.Forbidden, "格式化文件系统 — 不可逆整盘操作"),
            ["fdisk"] = new("fdisk", CommandRisk.SystemModification, CommandDangerLevel.Forbidden, "磁盘分区操作 — 不可逆整盘操作"),
            ["shred"] = new("shred", CommandRisk.DataModification, CommandDangerLevel.Forbidden, "安全擦除文件 — 数据不可恢复"),
            ["wipe"] = new("wipe", CommandRisk.DataModification, CommandDangerLevel.Forbidden, "安全擦除 — 数据不可恢复"),

            // === Critical（极危险，需显式确认，不可批量批准）— 不可逆系统操作 ===
            ["format"] = new("format", CommandRisk.SystemModification, CommandDangerLevel.Critical, "格式化磁盘 — 不可逆操作"),
            ["diskpart"] = new("diskpart", CommandRisk.SystemModification, CommandDangerLevel.Critical, "磁盘分区管理 — 可能清盘"),
            ["dd"] = new("dd", CommandRisk.DataModification, CommandDangerLevel.Critical, "直接磁盘读写 — 可能覆盖整盘"),
            ["shutdown"] = new("shutdown", CommandRisk.SystemModification, CommandDangerLevel.Critical, "系统关机 — 不可逆操作"),
            ["reboot"] = new("reboot", CommandRisk.SystemModification, CommandDangerLevel.Critical, "系统重启 — 不可逆操作"),
            ["halt"] = new("halt", CommandRisk.SystemModification, CommandDangerLevel.Critical, "系统停机 — 不可逆操作"),
            ["reg"] = new("reg", CommandRisk.SystemModification, CommandDangerLevel.Critical, "注册表操作 — 影响系统配置"),
            ["regedit"] = new("regedit", CommandRisk.SystemModification, CommandDangerLevel.Critical, "注册表编辑器 — 影响系统配置"),
            ["systemctl"] = new("systemctl", CommandRisk.SystemModification, CommandDangerLevel.Critical, "系统服务管理 — 影响系统运行"),
            ["iptables"] = new("iptables", CommandRisk.SystemModification, CommandDangerLevel.Critical, "防火墙规则 — 影响网络安全"),
            ["netsh"] = new("netsh", CommandRisk.SystemModification, CommandDangerLevel.Critical, "网络配置 — 影响系统网络"),
            ["schtasks"] = new("schtasks", CommandRisk.SystemModification, CommandDangerLevel.Critical, "计划任务 — 影响系统调度"),
            ["crontab"] = new("crontab", CommandRisk.SystemModification, CommandDangerLevel.Critical, "定时任务 — 影响系统调度"),
            ["sc"] = new("sc", CommandRisk.SystemModification, CommandDangerLevel.Critical, "服务控制 — 影响系统服务"),

            // === Dangerous（危险，需确认）— 文件删除操作，引导移动到 .xxx/ ===
            ["rm"] = new("rm", CommandRisk.FileDeletion, CommandDangerLevel.Dangerous, "文件删除 — 建议移动到 .xxx/ 目录"),
            ["del"] = new("del", CommandRisk.FileDeletion, CommandDangerLevel.Dangerous, "文件删除 — 建议移动到 .xxx/ 目录"),
            ["erase"] = new("erase", CommandRisk.FileDeletion, CommandDangerLevel.Dangerous, "文件删除 — 建议移动到 .xxx/ 目录"),
            ["Remove-Item"] = new("Remove-Item", CommandRisk.FileDeletion, CommandDangerLevel.Dangerous, "文件删除 — 建议移动到 .xxx/ 目录"),
            ["rmdir"] = new("rmdir", CommandRisk.DirectoryDeletion, CommandDangerLevel.Dangerous, "目录删除 — 建议移动到 .xxx/ 目录"),
            ["rd"] = new("rd", CommandRisk.DirectoryDeletion, CommandDangerLevel.Dangerous, "目录删除 — 建议移动到 .xxx/ 目录"),

            // === Dangerous（危险，需确认）— 数据移动/复制 ===
            ["mv"] = new("mv", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "文件移动 — 工作目录内安全，超范围需确认"),
            ["move"] = new("move", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "文件移动 — 工作目录内安全，超范围需确认"),
            ["Rename-Item"] = new("Rename-Item", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "文件重命名 — 工作目录内安全，超范围需确认"),
            ["cp"] = new("cp", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "文件复制 — 工作目录内安全，超范围需确认"),
            ["copy"] = new("copy", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "文件复制 — 工作目录内安全，超范围需确认"),
            ["xcopy"] = new("xcopy", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "文件复制 — 工作目录内安全，超范围需确认"),
            ["robocopy"] = new("robocopy", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "文件复制 — 工作目录内安全，超范围需确认"),
            ["Copy-Item"] = new("Copy-Item", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "文件复制 — 工作目录内安全，超范围需确认"),

            // === Dangerous（危险，需确认）— 权限修改 ===
            ["chmod"] = new("chmod", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "权限修改 — 影响文件访问控制"),
            ["chown"] = new("chown", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "属主修改 — 影响文件访问控制"),
            ["chgrp"] = new("chgrp", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "属组修改 — 影响文件访问控制"),
            ["attrib"] = new("attrib", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "属性修改 — 影响文件访问控制"),
            ["cacls"] = new("cacls", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "ACL 修改 — 影响文件访问控制"),
            ["icacls"] = new("icacls", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "ACL 修改 — 影响文件访问控制"),

            // === Dangerous（危险，需确认）— 进程终止 ===
            ["kill"] = new("kill", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "进程终止 — 可能影响系统稳定性"),
            ["killall"] = new("killall", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "批量进程终止 — 可能影响系统稳定性"),
            ["taskkill"] = new("taskkill", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "进程终止 — 可能影响系统稳定性"),

            // === Dangerous（危险，需确认）— 权限提升 ===
            ["sudo"] = new("sudo", CommandRisk.PrivilegeEscalation, CommandDangerLevel.Dangerous, "权限提升 — 以 root 执行命令"),
            ["runas"] = new("runas", CommandRisk.PrivilegeEscalation, CommandDangerLevel.Dangerous, "权限提升 — 以其他用户执行"),
            ["Start-Process"] = new("Start-Process", CommandRisk.PrivilegeEscalation, CommandDangerLevel.Dangerous, "启动进程 — 可能提权执行"),

            // === Dangerous（危险，需确认）— 远程执行 ===
            ["curl"] = new("curl", CommandRisk.RemoteExecution, CommandDangerLevel.Dangerous, "远程请求 — 可能下载恶意内容"),
            ["wget"] = new("wget", CommandRisk.RemoteExecution, CommandDangerLevel.Dangerous, "远程下载 — 可能下载恶意内容"),
            ["Invoke-WebRequest"] = new("Invoke-WebRequest", CommandRisk.RemoteExecution, CommandDangerLevel.Dangerous, "远程请求 — 可能下载恶意内容"),
            ["Invoke-RestMethod"] = new("Invoke-RestMethod", CommandRisk.RemoteExecution, CommandDangerLevel.Dangerous, "远程请求 — 可能下载恶意内容"),
        };

        return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
