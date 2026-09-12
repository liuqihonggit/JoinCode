namespace Core.Security.DangerClassification;

/// <summary>
/// 危险命令定义 — 用 <see cref="DangerCommandAttribute"/> 标注命令,源码生成器扫描生成字典
/// </summary>
/// <remarks>
/// 字段值为命令名(如 "ls"),特性参数指定风险类型/危险等级/描述。
/// 命名约定:字段名用 PascalCase,连字符去除(Get-Content → GetContent)。
/// </remarks>
public static class DangerCommandDefinitions
{
    // === Safe（白灯 / 自动通过）— 常见只读命令 ===
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "列目录 — 只读")] public const string Ls = "ls";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "列目录 — 只读")] public const string Dir = "dir";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件 — 只读")] public const string Cat = "cat";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件 — 只读")] public const string Type = "type";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件头 — 只读")] public const string Head = "head";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件尾 — 只读")] public const string Tail = "tail";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文本搜索 — 只读")] public const string Grep = "grep";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文本搜索 — 只读")] public const string Findstr = "findstr";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "统计 — 只读")] public const string Wc = "wc";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "打印 — 只读（重定向写入由文件权限检查处理）")] public const string Echo = "echo";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "打印 — 只读")] public const string Printf = "printf";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "当前目录 — 只读")] public const string Pwd = "pwd";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "当前用户 — 只读")] public const string Whoami = "whoami";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "主机名 — 只读")] public const string Hostname = "hostname";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "日期 — 只读")] public const string Date = "date";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "系统信息 — 只读")] public const string Uname = "uname";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "查找命令 — 只读")] public const string Which = "which";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "查找命令 — 只读")] public const string Where = "where";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "磁盘空间 — 只读")] public const string Df = "df";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "目录大小 — 只读")] public const string Du = "du";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "内存 — 只读")] public const string Free = "free";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "进程列表 — 只读")] public const string Ps = "ps";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "网络测试 — 只读")] public const string Ping = "ping";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "读文件 — 只读")] public const string GetContent = "Get-Content";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "列目录 — 只读")] public const string GetChildItem = "Get-ChildItem";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "获取项 — 只读")] public const string GetItem = "Get-Item";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "当前目录 — 只读")] public const string GetLocation = "Get-Location";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "进程列表 — 只读")] public const string GetProcess = "Get-Process";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "服务列表 — 只读")] public const string GetService = "Get-Service";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "打印 — 只读")] public const string WriteOutput = "Write-Output";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "文本搜索 — 只读")] public const string SelectString = "Select-String";

    // === Safe（白灯 / 自动通过）— 常见开发工具命令 ===
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, ".NET CLI — 开发工具（build/run/test 等）")] public const string Dotnet = "dotnet";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Node.js — 开发工具")] public const string Node = "node";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "npm 包管理器 — 开发工具")] public const string Npm = "npm";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "npx 包执行器 — 开发工具")] public const string Npx = "npx";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Python — 开发工具")] public const string Python = "python";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Python3 — 开发工具")] public const string Python3 = "python3";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "pip 包管理器 — 开发工具")] public const string Pip = "pip";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Java — 开发工具")] public const string Java = "java";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Maven — 开发工具")] public const string Mvn = "mvn";
    [DangerCommand(CommandRisk.None, CommandDangerLevel.Safe, "Gradle — 开发工具")] public const string Gradle = "gradle";

    // === Dangerous（直接拒绝不提示）— 整盘/系统级不可逆操作 ===
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "格式化文件系统 — 不可逆整盘操作")] public const string Mkfs = "mkfs";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Dangerous, "磁盘分区操作 — 不可逆整盘操作")] public const string Fdisk = "fdisk";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Dangerous, "安全擦除文件 — 数据不可恢复")] public const string Shred = "shred";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Dangerous, "安全擦除 — 数据不可恢复")] public const string Wipe = "wipe";

    // === LightValidation（绿灯ask / 可撤回）— git 操作 ===
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.LightValidation, "git 操作 — 可撤回（git reset --hard 等破坏性操作在组合检测中升级）")] public const string Git = "git";

    // === Execution（红灯ask / 不可撤回）— 磁盘/系统操作 ===
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "格式化磁盘 — 不可逆操作")] public const string Format = "format";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "磁盘分区管理 — 可能清盘")] public const string Diskpart = "diskpart";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "直接磁盘读写 — 可能覆盖整盘")] public const string Dd = "dd";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统关机 — 不可逆操作")] public const string Shutdown = "shutdown";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统重启 — 不可逆操作")] public const string Reboot = "reboot";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统停机 — 不可逆操作")] public const string Halt = "halt";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "注册表操作 — 影响系统配置")] public const string Reg = "reg";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "注册表编辑器 — 影响系统配置")] public const string Regedit = "regedit";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "系统服务管理 — 影响系统运行")] public const string Systemctl = "systemctl";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "防火墙规则 — 影响网络安全")] public const string Iptables = "iptables";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "网络配置 — 影响系统网络")] public const string Netsh = "netsh";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务 — 影响系统调度")] public const string Schtasks = "schtasks";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "定时任务 — 影响系统调度")] public const string Crontab = "crontab";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "服务控制 — 影响系统服务")] public const string Sc = "sc";

    // === Execution（红灯ask / 不可撤回）— 文件删除操作 ===
    [DangerCommand(CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Rm = "rm";
    [DangerCommand(CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Del = "del";
    [DangerCommand(CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Erase = "erase";
    [DangerCommand(CommandRisk.FileDeletion, CommandDangerLevel.Execution, "文件删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string RemoveItem = "Remove-Item";
    [DangerCommand(CommandRisk.DirectoryDeletion, CommandDangerLevel.Execution, "目录删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Rmdir = "rmdir";
    [DangerCommand(CommandRisk.DirectoryDeletion, CommandDangerLevel.Execution, "目录删除 — 不可撤回，建议移动到 .xxx/ 目录")] public const string Rd = "rd";

    // === Execution（红灯ask / 不可撤回）— 数据移动/复制 ===
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件移动 — 不可撤回")] public const string Mv = "mv";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件移动 — 不可撤回")] public const string Move = "move";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件重命名 — 不可撤回")] public const string RenameItem = "Rename-Item";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string Cp = "cp";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string Copy = "copy";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string Xcopy = "xcopy";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string Robocopy = "robocopy";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "文件复制 — 不可撤回")] public const string CopyItem = "Copy-Item";

    // === Execution（红灯ask / 不可撤回）— 权限修改 ===
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "权限修改 — 不可撤回")] public const string Chmod = "chmod";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "属主修改 — 不可撤回")] public const string Chown = "chown";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "属组修改 — 不可撤回")] public const string Chgrp = "chgrp";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "属性修改 — 不可撤回")] public const string Attrib = "attrib";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "ACL 修改 — 不可撤回")] public const string Cacls = "cacls";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "ACL 修改 — 不可撤回")] public const string Icacls = "icacls";

    // === Execution（红灯ask / 不可撤回）— 进程终止 ===
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "进程终止 — 不可撤回")] public const string Kill = "kill";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "批量进程终止 — 不可撤回")] public const string Killall = "killall";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "进程终止 — 不可撤回")] public const string Taskkill = "taskkill";

    // === Execution（红灯ask / 不可撤回）— 权限提升 ===
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — 以 root 执行命令")] public const string Sudo = "sudo";
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "权限提升 — 以其他用户执行")] public const string Runas = "runas";
    [DangerCommand(CommandRisk.PrivilegeEscalation, CommandDangerLevel.Execution, "启动进程 — 可能提权执行")] public const string StartProcess = "Start-Process";

    // === Execution（红灯ask / 不可撤回）— 远程执行/联网 ===
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回")] public const string Curl = "curl";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程下载 — 不可撤回")] public const string Wget = "wget";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回")] public const string InvokeWebRequest = "Invoke-WebRequest";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程请求 — 不可撤回")] public const string InvokeRestMethod = "Invoke-RestMethod";

    // === Execution（红灯ask / 不可撤回）— PowerShell 危险 cmdlet（集成自 PsDangerousCmdlets）===
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程命令执行 — 可执行任意代码")] public const string InvokeCommand = "Invoke-Command";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "表达式执行 — 可执行任意代码")] public const string InvokeExpression = "Invoke-Expression";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "后台作业 — 可执行任意代码")] public const string StartJob = "Start-Job";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "线程作业 — 可执行任意代码")] public const string StartThreadJob = "Start-ThreadJob";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "远程会话 — 可跨机器执行")] public const string NewPSSession = "New-PSSession";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "进入远程会话 — 可跨机器执行")] public const string EnterPSSession = "Enter-PSSession";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "引擎事件注册 — 回调可执行任意代码")] public const string RegisterEngineEvent = "Register-EngineEvent";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "对象事件注册 — 回调可执行任意代码")] public const string RegisterObjectEvent = "Register-ObjectEvent";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "WMI 事件注册 — 回调可执行任意代码")] public const string RegisterWmiEvent = "Register-WmiEvent";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块加载 — 模块可执行任意代码")] public const string ImportModule = "Import-Module";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Import-Module 别名 — 模块加载")] public const string Ipmo = "ipmo";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块安装 — 从远程仓库下载执行")] public const string InstallModule = "Install-Module";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块保存 — 从远程仓库下载")] public const string SaveModule = "Save-Module";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "模块更新 — 从远程仓库下载执行")] public const string UpdateModule = "Update-Module";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "脚本安装 — 从远程仓库下载执行")] public const string InstallScript = "Install-Script";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "脚本保存 — 从远程仓库下载")] public const string SaveScript = "Save-Script";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "WMI 方法调用 — 可生成进程")] public const string InvokeWmiMethod = "Invoke-WmiMethod";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Invoke-WmiMethod 别名 — WMI 方法调用")] public const string Iwmi = "iwmi";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "CIM 方法调用 — 可生成进程")] public const string InvokeCimMethod = "Invoke-CimMethod";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务注册 — 持久化执行")] public const string RegisterScheduledTask = "Register-ScheduledTask";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务创建 — 持久化执行")] public const string NewScheduledTask = "New-ScheduledTask";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务动作 — 持久化执行")] public const string NewScheduledTaskAction = "New-ScheduledTaskAction";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划任务设置 — 持久化执行")] public const string SetScheduledTask = "Set-ScheduledTask";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "计划作业注册 — 持久化执行")] public const string RegisterScheduledJob = "Register-ScheduledJob";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "别名设置 — 可劫持命令解析")] public const string SetAlias = "Set-Alias";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "Set-Alias 别名 — 可劫持命令解析")] public const string Sal = "sal";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "别名创建 — 可劫持命令解析")] public const string NewAlias = "New-Alias";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "New-Alias 别名 — 可劫持命令解析")] public const string Nal = "nal";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "变量设置 — 可劫持命令解析")] public const string SetVariable = "Set-Variable";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "Set-Variable 别名 — 可劫持命令解析")] public const string Sv = "sv";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "变量创建 — 可劫持命令解析")] public const string NewVariable = "New-Variable";
    [DangerCommand(CommandRisk.SystemModification, CommandDangerLevel.Execution, "New-Variable 别名 — 可劫持命令解析")] public const string Nv = "nv";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "项设置 — 数据修改")] public const string SetItem = "Set-Item";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "Set-Item 别名 — 数据修改")] public const string Si = "si";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "项创建 — 数据修改")] public const string NewItem = "New-Item";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "New-Item 别名 — 数据修改")] public const string Ni = "ni";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "项清除 — 数据修改")] public const string ClearItem = "Clear-Item";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "Clear-Item 别名 — 数据修改")] public const string Cli = "cli";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "内容写入 — 数据修改")] public const string SetContent = "Set-Content";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "内容追加 — 数据修改")] public const string AddContent = "Add-Content";
    [DangerCommand(CommandRisk.DataModification, CommandDangerLevel.Execution, "Add-Content 别名 — 数据修改")] public const string Ac = "ac";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Invoke-WebRequest 别名 — 远程请求")] public const string Iwr = "iwr";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "Invoke-RestMethod 别名 — 远程请求")] public const string Irm = "irm";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "对象创建 — 可实例化 COM/ActiveX")] public const string NewObject = "New-Object";
    [DangerCommand(CommandRisk.RemoteExecution, CommandDangerLevel.Execution, "BITS 传输 — 后台远程下载")] public const string StartBitsTransfer = "Start-BitsTransfer";
}
