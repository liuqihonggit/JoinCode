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
public enum CommandCategory
{
    /// <summary>
    /// 未知命令
    /// </summary>
    Unknown,

    /// <summary>
    /// 只读命令，安全执行
    /// </summary>
    ReadOnly,

    /// <summary>
    /// 破坏性命令，需要确认
    /// </summary>
    Destructive,

    /// <summary>
    /// 路径违规命令，操作超出工作区
    /// </summary>
    PathViolation
}

/// <summary>
/// 命令风险类型
/// </summary>
public enum CommandRisk
{
    /// <summary>
    /// 无风险
    /// </summary>
    None,

    /// <summary>
    /// 文件删除风险
    /// </summary>
    FileDeletion,

    /// <summary>
    /// 目录删除风险
    /// </summary>
    DirectoryDeletion,

    /// <summary>
    /// 数据修改风险
    /// </summary>
    DataModification,

    /// <summary>
    /// 系统修改风险
    /// </summary>
    SystemModification,

    /// <summary>
    /// 路径越界风险
    /// </summary>
    PathEscape,

    /// <summary>
    /// 递归操作风险
    /// </summary>
    RecursiveOperation,

    /// <summary>
    /// 强制操作风险
    /// </summary>
    ForceOperation,

    /// <summary>
    /// 远程代码执行风险
    /// </summary>
    RemoteExecution,

    /// <summary>
    /// 权限提升风险
    /// </summary>
    PrivilegeEscalation
}
