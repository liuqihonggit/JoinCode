namespace JoinCode.Abstractions.Models.Skill;

/// <summary>
/// 代码验证类型
/// </summary>
public enum CodeVerifyType
{
    [EnumValue("all")] All,
    [EnumValue("syntax")] Syntax,
    [EnumValue("build")] Build,
    [EnumValue("test")] Test
}

/// <summary>
/// 代码简化类型
/// </summary>
public enum CodeSimplifyType
{
    [EnumValue("all")] All,
    [EnumValue("readability")] Readability,
    [EnumValue("performance")] Performance,
    [EnumValue("complexity")] Complexity
}

/// <summary>
/// 诊断类型
/// </summary>
public enum CodeDebugType
{
    [EnumValue("all")] All,
    [EnumValue("error")] Error,
    [EnumValue("performance")] Performance,
    [EnumValue("memory")] Memory
}

/// <summary>
/// 批量文件操作类型
/// </summary>
public enum BatchOperationType
{
    [EnumValue("count")] Count,
    [EnumValue("search")] Search,
    [EnumValue("replace")] Replace,
    [EnumValue("delete")] Delete
}
