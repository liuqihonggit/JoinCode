namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// 危险 PS cmdlet 常量集 — 与 TS dangerousCmdlets.ts 1:1 对齐
/// </summary>
public static partial class PsDangerousCmdlets
{
    /// <summary>
    /// 接受 -FilePath 并执行脚本文件的 cmdlet
    /// </summary>
    public static readonly FrozenSet<string> FilePathExecution = FrozenSet.ToFrozenSet(
    [
        "invoke-command", "start-job", "start-threadjob", "register-scheduledjob",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 脚本块参数可执行任意代码的 cmdlet
    /// </summary>
    public static readonly FrozenSet<string> DangerousScriptBlock = FrozenSet.ToFrozenSet(
    [
        "invoke-command", "invoke-expression", "start-job", "start-threadjob",
        "register-scheduledjob", "register-engineevent", "register-objectevent",
        "register-wmievent", "new-pssession", "enter-pssession",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 加载/执行模块代码的 cmdlet
    /// </summary>
    public static readonly FrozenSet<string> ModuleLoading = FrozenSet.ToFrozenSet(
    [
        "import-module", "ipmo", "install-module", "save-module",
        "update-module", "install-script", "save-script",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 网络 cmdlet — 通配符规则可导致数据泄露/下载
    /// </summary>
    public static readonly FrozenSet<string> Network = FrozenSet.ToFrozenSet(
    [
        "invoke-webrequest", "invoke-restmethod",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 别名/变量劫持 cmdlet
    /// </summary>
    public static readonly FrozenSet<string> AliasHijack = FrozenSet.ToFrozenSet(
    [
        "set-alias", "sal", "new-alias", "nal",
        "set-variable", "sv", "new-variable", "nv",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// WMI/CIM 进程生成 cmdlet
    /// </summary>
    public static readonly FrozenSet<string> WmiCim = FrozenSet.ToFrozenSet(
    [
        "invoke-wmimethod", "iwmi", "invoke-cimmethod",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 安全的脚本块消费 cmdlet（过滤/输出，非执行）
    /// </summary>
    public static readonly FrozenSet<string> SafeScriptBlock = FrozenSet.ToFrozenSet(
    [
        "where-object", "sort-object", "select-object", "group-object",
        "format-table", "format-list", "format-wide", "format-custom",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 下载器命令名
    /// </summary>
    public static readonly FrozenSet<string> DownloaderNames = FrozenSet.ToFrozenSet(
    [
        "invoke-webrequest", "iwr", "invoke-restmethod", "irm",
        "new-object", "start-bitstransfer",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 计划任务 cmdlet
    /// </summary>
    public static readonly FrozenSet<string> ScheduledTask = FrozenSet.ToFrozenSet(
    [
        "register-scheduledtask", "new-scheduledtask",
        "new-scheduledtaskaction", "set-scheduledtask",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 环境变量写入 cmdlet
    /// </summary>
    public static readonly FrozenSet<string> EnvWrite = FrozenSet.ToFrozenSet(
    [
        "set-item", "si", "new-item", "ni", "remove-item", "ri",
        "del", "rm", "rd", "rmdir", "erase", "clear-item", "cli",
        "set-content", "add-content", "ac",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 运行时状态操纵 cmdlet
    /// </summary>
    public static readonly FrozenSet<string> RuntimeState = FrozenSet.ToFrozenSet(
    [
        "set-alias", "sal", "new-alias", "nal",
        "set-variable", "sv", "new-variable", "nv",
    ], StringComparer.OrdinalIgnoreCase);
}
