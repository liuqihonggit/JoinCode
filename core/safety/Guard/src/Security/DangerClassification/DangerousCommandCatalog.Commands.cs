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

            // === Safe（白灯 / 自动通过）— 常见开发工具命令，AI 工具内部调用需要 ===
            ["dotnet"] = new("dotnet", CommandRisk.None, CommandDangerLevel.Safe, ".NET CLI — 开发工具（build/run/test 等）"),
            ["node"] = new("node", CommandRisk.None, CommandDangerLevel.Safe, "Node.js — 开发工具"),
            ["npm"] = new("npm", CommandRisk.None, CommandDangerLevel.Safe, "npm 包管理器 — 开发工具"),
            ["npx"] = new("npx", CommandRisk.None, CommandDangerLevel.Safe, "npx 包执行器 — 开发工具"),
            ["python"] = new("python", CommandRisk.None, CommandDangerLevel.Safe, "Python — 开发工具"),
            ["python3"] = new("python3", CommandRisk.None, CommandDangerLevel.Safe, "Python3 — 开发工具"),
            ["pip"] = new("pip", CommandRisk.None, CommandDangerLevel.Safe, "pip 包管理器 — 开发工具"),
            ["java"] = new("java", CommandRisk.None, CommandDangerLevel.Safe, "Java — 开发工具"),
            ["mvn"] = new("mvn", CommandRisk.None, CommandDangerLevel.Safe, "Maven — 开发工具"),
            ["gradle"] = new("gradle", CommandRisk.None, CommandDangerLevel.Safe, "Gradle — 开发工具"),

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

            // === Execution（红灯ask / 不可撤回）— PowerShell 危险 cmdlet（集成自 PsDangerousCmdlets）===
            // 脚本执行/远程调用
            ["Invoke-Command"] = new("Invoke-Command", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程命令执行 — 可执行任意代码"),
            ["Invoke-Expression"] = new("Invoke-Expression", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "表达式执行 — 可执行任意代码"),
            ["Start-Job"] = new("Start-Job", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "后台作业 — 可执行任意代码"),
            ["Start-ThreadJob"] = new("Start-ThreadJob", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "线程作业 — 可执行任意代码"),
            ["New-PSSession"] = new("New-PSSession", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程会话 — 可跨机器执行"),
            ["Enter-PSSession"] = new("Enter-PSSession", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "进入远程会话 — 可跨机器执行"),
            // 事件注册（可执行回调脚本块）
            ["Register-EngineEvent"] = new("Register-EngineEvent", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "引擎事件注册 — 回调可执行任意代码"),
            ["Register-ObjectEvent"] = new("Register-ObjectEvent", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "对象事件注册 — 回调可执行任意代码"),
            ["Register-WmiEvent"] = new("Register-WmiEvent", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "WMI 事件注册 — 回调可执行任意代码"),
            // 模块加载（模块可执行任意代码）
            ["Import-Module"] = new("Import-Module", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块加载 — 模块可执行任意代码"),
            ["ipmo"] = new("ipmo", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Import-Module 别名 — 模块加载"),
            ["Install-Module"] = new("Install-Module", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块安装 — 从远程仓库下载执行"),
            ["Save-Module"] = new("Save-Module", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块保存 — 从远程仓库下载"),
            ["Update-Module"] = new("Update-Module", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块更新 — 从远程仓库下载执行"),
            ["Install-Script"] = new("Install-Script", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "脚本安装 — 从远程仓库下载执行"),
            ["Save-Script"] = new("Save-Script", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "脚本保存 — 从远程仓库下载"),
            // WMI/CIM 进程生成
            ["Invoke-WmiMethod"] = new("Invoke-WmiMethod", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "WMI 方法调用 — 可生成进程"),
            ["iwmi"] = new("iwmi", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Invoke-WmiMethod 别名 — WMI 方法调用"),
            ["Invoke-CimMethod"] = new("Invoke-CimMethod", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "CIM 方法调用 — 可生成进程"),
            // 计划任务（持久化）
            ["Register-ScheduledTask"] = new("Register-ScheduledTask", CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务注册 — 持久化执行"),
            ["New-ScheduledTask"] = new("New-ScheduledTask", CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务创建 — 持久化执行"),
            ["New-ScheduledTaskAction"] = new("New-ScheduledTaskAction", CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务动作 — 持久化执行"),
            ["Set-ScheduledTask"] = new("Set-ScheduledTask", CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务设置 — 持久化执行"),
            ["Register-ScheduledJob"] = new("Register-ScheduledJob", CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划作业注册 — 持久化执行"),
            // 别名/变量劫持（命令解析劫持）
            ["Set-Alias"] = new("Set-Alias", CommandRisk.SystemModification, CommandDangerLevel.Execution, "别名设置 — 可劫持命令解析"),
            ["sal"] = new("sal", CommandRisk.SystemModification, CommandDangerLevel.Execution, "Set-Alias 别名 — 可劫持命令解析"),
            ["New-Alias"] = new("New-Alias", CommandRisk.SystemModification, CommandDangerLevel.Execution, "别名创建 — 可劫持命令解析"),
            ["nal"] = new("nal", CommandRisk.SystemModification, CommandDangerLevel.Execution, "New-Alias 别名 — 可劫持命令解析"),
            ["Set-Variable"] = new("Set-Variable", CommandRisk.SystemModification, CommandDangerLevel.Execution, "变量设置 — 可劫持命令解析"),
            ["sv"] = new("sv", CommandRisk.SystemModification, CommandDangerLevel.Execution, "Set-Variable 别名 — 可劫持命令解析"),
            ["New-Variable"] = new("New-Variable", CommandRisk.SystemModification, CommandDangerLevel.Execution, "变量创建 — 可劫持命令解析"),
            ["nv"] = new("nv", CommandRisk.SystemModification, CommandDangerLevel.Execution, "New-Variable 别名 — 可劫持命令解析"),
            // 环境变量/项写入
            ["Set-Item"] = new("Set-Item", CommandRisk.DataModification, CommandDangerLevel.Execution, "项设置 — 数据修改"),
            ["si"] = new("si", CommandRisk.DataModification, CommandDangerLevel.Execution, "Set-Item 别名 — 数据修改"),
            ["New-Item"] = new("New-Item", CommandRisk.DataModification, CommandDangerLevel.Execution, "项创建 — 数据修改"),
            ["ni"] = new("ni", CommandRisk.DataModification, CommandDangerLevel.Execution, "New-Item 别名 — 数据修改"),
            ["Clear-Item"] = new("Clear-Item", CommandRisk.DataModification, CommandDangerLevel.Execution, "项清除 — 数据修改"),
            ["cli"] = new("cli", CommandRisk.DataModification, CommandDangerLevel.Execution, "Clear-Item 别名 — 数据修改"),
            ["Set-Content"] = new("Set-Content", CommandRisk.DataModification, CommandDangerLevel.Execution, "内容写入 — 数据修改"),
            ["Add-Content"] = new("Add-Content", CommandRisk.DataModification, CommandDangerLevel.Execution, "内容追加 — 数据修改"),
            ["ac"] = new("ac", CommandRisk.DataModification, CommandDangerLevel.Execution, "Add-Content 别名 — 数据修改"),
            // 下载器别名/补充
            ["iwr"] = new("iwr", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Invoke-WebRequest 别名 — 远程请求"),
            ["irm"] = new("irm", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Invoke-RestMethod 别名 — 远程请求"),
            ["New-Object"] = new("New-Object", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "对象创建 — 可实例化 COM/ActiveX"),
            ["Start-BitsTransfer"] = new("Start-BitsTransfer", CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "BITS 传输 — 后台远程下载"),
        };

        return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
