namespace Core.Security.DangerClassification;

/// <summary>
/// 命令映射表构建 — 所有危险命令的统一定义
/// 5级分级: Safe(白灯只读自动通过) / Unknown(黄灯未知命令ask) / LightValidation(绿灯可撤回ask) / Execution(红灯不可撤回ask) / Dangerous(黑灯直接拒绝)
/// 核心区分: 黄灯=未知命令需确认, 绿灯=可撤回操作(git/跨目录读取), 红灯=不可撤回操作(删除/联网/系统修改), 黑灯=直接拒绝
/// </summary>
public static partial class DangerousCommandCatalog
{
    private static FrozenDictionary<string, CommandEntry> BuildCommands()
    {
        var entries = new Dictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase)
        {
            // === Safe（白灯 / 自动通过）— 常见只读命令，显式登记避免被误判为 Unknown（黄灯）===
            ["ls"] = new("ls", CommandRisk.None, CommandDangerLevel.Safe, "列目录 — 只读"),
            ["dir"] = new("dir", CommandRisk.None, CommandDangerLevel.Safe, "列目录 — 只读"),
            ["cat"] = new("cat", CommandRisk.None, CommandDangerLevel.Safe, "读文件 — 只读"),
            ["type"] = new("type", CommandRisk.None, CommandDangerLevel.Safe, "读文件 — 只读"),
            ["head"] = new("head", CommandRisk.None, CommandDangerLevel.Safe, "读文件头 — 只读"),
            ["tail"] = new("tail", CommandRisk.None, CommandDangerLevel.Safe, "读文件尾 — 只读"),
            ["grep"] = new("grep", CommandRisk.None, CommandDangerLevel.Safe, "文本搜索 — 只读"),
            ["findstr"] = new("findstr", CommandRisk.None, CommandDangerLevel.Safe, "文本搜索 — 只读"),
            ["wc"] = new("wc", CommandRisk.None, CommandDangerLevel.Safe, "统计 — 只读"),
            ["echo"] = new("echo", CommandRisk.None, CommandDangerLevel.Safe, "打印 — 只读（重定向写入由文件权限检查处理）"),
            ["printf"] = new("printf", CommandRisk.None, CommandDangerLevel.Safe, "打印 — 只读"),
            ["pwd"] = new("pwd", CommandRisk.None, CommandDangerLevel.Safe, "当前目录 — 只读"),
            ["whoami"] = new("whoami", CommandRisk.None, CommandDangerLevel.Safe, "当前用户 — 只读"),
            ["hostname"] = new("hostname", CommandRisk.None, CommandDangerLevel.Safe, "主机名 — 只读"),
            ["date"] = new("date", CommandRisk.None, CommandDangerLevel.Safe, "日期 — 只读"),
            ["uname"] = new("uname", CommandRisk.None, CommandDangerLevel.Safe, "系统信息 — 只读"),
            ["which"] = new("which", CommandRisk.None, CommandDangerLevel.Safe, "查找命令 — 只读"),
            ["where"] = new("where", CommandRisk.None, CommandDangerLevel.Safe, "查找命令 — 只读"),
            ["df"] = new("df", CommandRisk.None, CommandDangerLevel.Safe, "磁盘空间 — 只读"),
            ["du"] = new("du", CommandRisk.None, CommandDangerLevel.Safe, "目录大小 — 只读"),
            ["free"] = new("free", CommandRisk.None, CommandDangerLevel.Safe, "内存 — 只读"),
            ["ps"] = new("ps", CommandRisk.None, CommandDangerLevel.Safe, "进程列表 — 只读"),
            ["ping"] = new("ping", CommandRisk.None, CommandDangerLevel.Safe, "网络测试 — 只读"),
            ["Get-Content"] = new("Get-Content", CommandRisk.None, CommandDangerLevel.Safe, "读文件 — 只读"),
            ["Get-ChildItem"] = new("Get-ChildItem", CommandRisk.None, CommandDangerLevel.Safe, "列目录 — 只读"),
            ["Get-Item"] = new("Get-Item", CommandRisk.None, CommandDangerLevel.Safe, "获取项 — 只读"),
            ["Get-Location"] = new("Get-Location", CommandRisk.None, CommandDangerLevel.Safe, "当前目录 — 只读"),
            ["Get-Process"] = new("Get-Process", CommandRisk.None, CommandDangerLevel.Safe, "进程列表 — 只读"),
            ["Get-Service"] = new("Get-Service", CommandRisk.None, CommandDangerLevel.Safe, "服务列表 — 只读"),
            ["Write-Output"] = new("Write-Output", CommandRisk.None, CommandDangerLevel.Safe, "打印 — 只读"),
            ["Select-String"] = new("Select-String", CommandRisk.None, CommandDangerLevel.Safe, "文本搜索 — 只读"),

            // === Dangerous（直接拒绝不提示）— 整盘/系统级不可逆操作 ===
            ["mkfs"] = new("mkfs", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "格式化文件系统 — 不可逆整盘操作"),
            ["fdisk"] = new("fdisk", CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "磁盘分区操作 — 不可逆整盘操作"),
            ["shred"] = new("shred", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "安全擦除文件 — 数据不可恢复"),
            ["wipe"] = new("wipe", CommandRisk.DataModification, CommandDangerLevel.Dangerous, "安全擦除 — 数据不可恢复"),

            // === LightValidation（绿灯ask / 可撤回）— git 操作，可通过 git reset/reflog 撤回 ===
            ["git"] = new("git", CommandRisk.DataModification, CommandDangerLevel.LightValidation, "git 操作 — 可撤回（git reset --hard 等破坏性操作在组合检测中升级）"),

            // === Execution（红灯ask / 不可撤回）— 磁盘/系统操作 ===
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

            // === Execution（红灯ask / 不可撤回）— 文件删除操作 ===
            ["rm"] = new("rm", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["del"] = new("del", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["erase"] = new("erase", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["Remove-Item"] = new("Remove-Item", CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["rmdir"] = new("rmdir", CommandRisk.DirectoryDeletion, CommandDangerLevel.Execution, "目录删除 — 不可撤回，建议移动到 .xxx/ 目录"),
            ["rd"] = new("rd", CommandRisk.DirectoryDeletion, CommandDangerLevel.Execution, "目录删除 — 不可撤回，建议移动到 .xxx/ 目录"),

            // === Execution（红灯ask / 不可撤回）— 数据移动/复制 ===
            ["mv"] = new("mv", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件移动 — 不可撤回"),
            ["move"] = new("move", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件移动 — 不可撤回"),
            ["Rename-Item"] = new("Rename-Item", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件重命名 — 不可撤回"),
            ["cp"] = new("cp", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),
            ["copy"] = new("copy", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),
            ["xcopy"] = new("xcopy", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),
            ["robocopy"] = new("robocopy", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),
            ["Copy-Item"] = new("Copy-Item", CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回"),

            // === Execution（红灯ask / 不可撤回）— 权限修改 ===
            ["chmod"] = new("chmod", CommandRisk.SystemModification, CommandDangerLevel.Execution, "权限修改 — 不可撤回"),
            ["chown"] = new("chown", CommandRisk.SystemModification, CommandDangerLevel.Execution, "属主修改 — 不可撤回"),
            ["chgrp"] = new("chgrp", CommandRisk.SystemModification, CommandDangerLevel.Execution, "属组修改 — 不可撤回"),
            ["attrib"] = new("attrib", CommandRisk.SystemModification, CommandDangerLevel.Execution, "属性修改 — 不可撤回"),
            ["cacls"] = new("cacls", CommandRisk.SystemModification, CommandDangerLevel.Execution, "ACL 修改 — 不可撤回"),
            ["icacls"] = new("icacls", CommandRisk.SystemModification, CommandDangerLevel.Execution, "ACL 修改 — 不可撤回"),

            // === Execution（红灯ask / 不可撤回）— 进程终止 ===
            ["kill"] = new("kill", CommandRisk.DataModification, CommandDangerLevel.Execution, "进程终止 — 不可撤回"),
            ["killall"] = new("killall", CommandRisk.DataModification, CommandDangerLevel.Execution, "批量进程终止 — 不可撤回"),
            ["taskkill"] = new("taskkill", CommandRisk.DataModification, CommandDangerLevel.Execution, "进程终止 — 不可撤回"),

            // === Execution（红灯ask / 不可撤回）— 权限提升 ===
            ["sudo"] = new("sudo", CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — 以 root 执行命令"),
            ["runas"] = new("runas", CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — 以其他用户执行"),
            ["Start-Process"] = new("Start-Process", CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "启动进程 — 可能提权执行"),

            // === Execution（红灯ask / 不可撤回）— 远程执行/联网 ===
            ["curl"] = new("curl", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回"),
            ["wget"] = new("wget", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程下载 — 不可撤回"),
            ["Invoke-WebRequest"] = new("Invoke-WebRequest", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回"),
            ["Invoke-RestMethod"] = new("Invoke-RestMethod", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回"),
        };

        return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
