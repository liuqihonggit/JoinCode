namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 命令分类结果
/// </summary>
public sealed record CommandClassification(
    CommandCategory Category,
    IReadOnlyList<CommandRisk> Risks,
    string? Details = null);

/// <summary>
/// 命令分类类别
/// </summary>
public enum CommandCategory {
    /// <summary>
    /// 未知命令
    /// </summary>
    [EnumValue("unknown")]
    Unknown,

    /// <summary>
    /// 只读命令，安全执行
    /// </summary>
    [EnumValue("read_only")]
    ReadOnly,

    /// <summary>
    /// 破坏性命令，需要确认
    /// </summary>
    [EnumValue("destructive")]
    Destructive,

    /// <summary>
    /// 路径违规命令，操作超出工作区
    /// </summary>
    [EnumValue("path_violation")]
    PathViolation,

    /// <summary>
    /// 搜索范围过大 — 使用危险标志（如 --no-ignore）或搜索系统目录（如 C:\、/），可能导致长时间卡顿
    /// </summary>
    [EnumValue("excessive_search_scope")]
    ExcessiveSearchScope
}

/// <summary>
/// 命令风险类型
/// </summary>
public enum CommandRisk {
    /// <summary>
    /// 无风险
    /// </summary>
    [EnumValue("none")]
    None,

    /// <summary>
    /// 文件删除风险
    /// </summary>
    [EnumValue("file_deletion")]
    FileDeletion,

    /// <summary>
    /// 目录删除风险
    /// </summary>
    [EnumValue("directory_deletion")]
    DirectoryDeletion,

    /// <summary>
    /// 数据修改风险
    /// </summary>
    [EnumValue("data_modification")]
    DataModification,

    /// <summary>
    /// 系统修改风险
    /// </summary>
    [EnumValue("system_modification")]
    SystemModification,

    /// <summary>
    /// 路径越界风险
    /// </summary>
    [EnumValue("path_escape")]
    PathEscape,

    /// <summary>
    /// 递归操作风险
    /// </summary>
    [EnumValue("recursive_operation")]
    RecursiveOperation,

    /// <summary>
    /// 强制操作风险
    /// </summary>
    [EnumValue("force_operation")]
    ForceOperation,

    /// <summary>
    /// 远程代码执行风险
    /// </summary>
    [EnumValue("remote_execution")]
    RemoteExecution,

    /// <summary>
    /// 权限提升风险
    /// </summary>
    [EnumValue("privilege_escalation")]
    PrivilegeEscalation,

    /// <summary>
    /// 搜索范围过大风险 — 可能导致命令长时间卡顿或消耗过多资源
    /// </summary>
    [EnumValue("excessive_search_scope")]
    ExcessiveSearchScope
}