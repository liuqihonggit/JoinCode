namespace JoinCode.Abstractions.Security;

/// <summary>
/// 工具操作类型枚举 — 替代散布在权限系统中的硬编码字符串
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 OperationTypeConstants + OperationTypeExtensions
/// </summary>
public enum OperationType
{
    /// <summary>读取操作</summary>
    [EnumValue("read")] Read = 0,

    /// <summary>写入操作</summary>
    [EnumValue("write")] Write = 1,

    /// <summary>编辑操作</summary>
    [EnumValue("edit")] Edit = 2,

    /// <summary>创建操作</summary>
    [EnumValue("create")] Create = 3,

    /// <summary>删除操作</summary>
    [EnumValue("delete")] Delete = 4,

    /// <summary>列出操作</summary>
    [EnumValue("list")] List = 5,

    /// <summary>获取操作</summary>
    [EnumValue("get")] Get = 6,

    /// <summary>搜索操作</summary>
    [EnumValue("search")] Search = 7,

    /// <summary>Glob 模式匹配</summary>
    [EnumValue("glob")] Glob = 8,

    /// <summary>Grep 文本搜索</summary>
    [EnumValue("grep")] Grep = 9,

    /// <summary>Bash 命令执行</summary>
    [EnumValue("bash")] Bash = 10,

    /// <summary>Shell 命令执行</summary>
    [EnumValue("shell")] Shell = 11,

    /// <summary>通用执行操作</summary>
    [EnumValue("execute")] Execute = 12,

    /// <summary>运行操作</summary>
    [EnumValue("run")] Run = 13,

    /// <summary>自动检测（分类器无法确定具体类型时使用）</summary>
    [EnumValue("auto")] Auto = 14
}
