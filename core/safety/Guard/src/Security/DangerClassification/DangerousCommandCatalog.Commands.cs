namespace Core.Security.DangerClassification;

/// <summary>
/// 命令映射表构建 — 所有危险命令的统一定义
/// 4级分级: Safe(只读自动通过) / LightValidation(绿色ask可撤回) / Execution(红色ask不可撤回) / Dangerous(直接拒绝)
/// 核心区分: 绿色ask=可撤回操作(git/跨目录读取), 红色ask=不可撤回操作(删除/联网/系统修改)
/// </summary>
public static partial class DangerousCommandCatalog
{
    private static FrozenDictionary<string, CommandEntry> BuildCommands()
    {
        var entries = new Dictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase)
        {
            // === Dangerous（直接拒绝不提示）— 整盘/系统级不可逆操作 ===
            ["mkfs"] = new("mkfs", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "格式化文件系统 — 不可逆整盘操作"),
            ["fdisk"] = new("fdisk", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "磁盘分区操作 — 不可逆整盘操作"),
            ["shred"] = new("shred", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "安全擦除文件 — 数据不可恢复"),
            ["wipe"] = new("wipe", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "安全擦除 — 数据不可恢复"),

            // === LightValidation（绿色 ask / 可撤回）— git 操作，可通过 git reset/reflog 撤回 ===
            ["git"] = new("git", CommandRisk.DataModification, CommandDangerLevel.LightValidation, "git 操作 — 可撤回（git reset --hard 等破坏性操作在组合检测中升级）"),

            // === Execution（红色 ask / 不可撤回）— 磁盘/系统操作 ===
            ["format"] = new("format", CommandRisk.SystemModification, CommandDangerLevel.Execution, "格式化磁盘 — 不可逆操作"),
            ["diskpart"] = new("diskpart", CommandRisk.SystemModification, CommandDangerLevel.Execution, "磁盘分区管理 — 可能清盘"),
            ["dd"] = new("dd", CommandRisk.DataModification, CommandDangerLevel.Execution, "直接磁盘读写 — 可能覆盖整盘"),
            ["shutdown"] = new("shutdown", CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统关机 — 不可逆操作"),
            ["reboot"] = new("reboot", CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统重启 — 不可逆操作"),
            ["halt"] = new("halt", CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统停机 — 不可逆操作"),
            ["reg"] = new("reg", CommandRisk.SystemModification, CommandDangerLevel.Execution, "注册表操作 — 影响系统配置"),
            ["regedit"] = new("regedit", CommandRisk.SystemModification, CommandDangerLevel.Execution, "注册表编辑器 — 影响系统配置"),
            ["systemctl"] = new("systemctl", CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统服务管理 — 影响系统运行"),
            ["iptables"] = new("iptables", CommandRisk.SystemModification, CommandDangerLevel.Execution, "防火墙规则 — 影响网络安全"),
            ["netsh"] = new("netsh", CommandRisk.SystemModification, CommandDangerLevel.Execution, "网络配置 — 影响系统网络"),
            ["schtasks"] = new("schtasks", CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务 — 影响系统调度"),
            ["crontab"] = new("crontab", CommandRisk.SystemModification, CommandDangerLevel.Execution, "定时任务 — 影响系统调度"),
            ["sc"] = new("sc", CommandRisk.SystemModification, CommandDangerLevel.Execution, "服务控制 — 影响系统服务"),

            // === Execution（红色 ask / 不可撤回）— 文件删除操作 ===
            ["rm"] = new("rm", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["del"] = new("del", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["erase"] = new("erase", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["Remove-Item"] = new("Remove-Item", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["rmdir"] = new("rmdir", CommandRisk.DirectoryDeletion, CommandDangerLevel.Execution, "目录删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["rd"] = new("rd", CommandRisk.DirectoryDeletion, CommandDangerLevel.Execution, "目录删除 — 不可撤回，建议移动到 .xxx/ 目录"),

            // === Execution（红色 ask / 不可撤回）— 数据移动/复制 ===
            ["mv"] = new("mv", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件移动 — 不可撤回"),
            ["move"] = new("move", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件移动 — 不可撤回"),
            ["Rename-Item"] = new("Rename-Item", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件重命名 — 不可撤回"),
            ["cp"] = new("cp", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),
            ["copy"] = new("copy", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),
            ["xcopy"] = new("xcopy", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),
            ["robocopy"] = new("robocopy", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),
            ["Copy-Item"] = new("Copy-Item", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),

            // === Execution（红色 ask / 不可撤回）— 权限修改 ===
            ["chmod"] = new("chmod", CommandRisk.SystemModification, CommandDangerLevel.Execution, "权限修改 — 不可撤回"),
            ["chown"] = new("chown", CommandRisk.SystemModification, CommandDangerLevel.Execution, "属主修改 — 不可撤回"),
            ["chgrp"] = new("chgrp", CommandRisk.SystemModification, CommandDangerLevel.Execution, "属组修改 — 不可撤回"),
            ["attrib"] = new("attrib", CommandRisk.SystemModification, CommandDangerLevel.Execution, "属性修改 — 不可撤回"),
            ["cacls"] = new("cacls", CommandRisk.SystemModification, CommandDangerLevel.Execution, "ACL 修改 — 不可撤回"),
            ["icacls"] = new("icacls", CommandRisk.SystemModification, CommandDangerLevel.Execution, "ACL 修改 — 不可撤回"),

            // === Execution（红色 ask / 不可撤回）— 进程终止 ===
            ["kill"] = new("kill", CommandRisk.DataModification, CommandDangerLevel.Execution, "进程终止 — 不可撤回"),
            ["killall"] = new("killall", CommandRisk.DataModification, CommandDangerLevel.Execution, "批量进程终止 — 不可撤回"),
            ["taskkill"] = new("taskkill", CommandRisk.DataModification, CommandDangerLevel.Execution, "进程终止 — 不可撤回"),

            // === Execution（红色 ask / 不可撤回）— 权限提升 ===
            ["sudo"] = new("sudo", CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — 以 root 执行命令"),
            ["runas"] = new("runas", CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — 以其他用户执行"),
            ["Start-Process"] = new("Start-Process", CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "启动进程 — 可能提权执行"),

            // === Execution（红色 ask / 不可撤回）— 远程执行/联网 ===
            ["curl"] = new("curl", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回"),
            ["wget"] = new("wget", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程下载 — 不可撤回"),
            ["Invoke-WebRequest"] = new("Invoke-WebRequest", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回"),
            ["Invoke-RestMethod"] = new("Invoke-RestMethod", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回"),
        };

        return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
