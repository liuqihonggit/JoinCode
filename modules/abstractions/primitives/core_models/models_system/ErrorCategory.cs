namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// 错误类别
/// </summary>
public enum ErrorCategory {
    [EnumValue("workflow")] Workflow,
    [EnumValue("configuration")] Configuration,
    [EnumValue("api")] Api,
    [EnumValue("code_execution")] CodeExecution,
    [EnumValue("permission")] Permission,
    [EnumValue("security")] Security,
    [EnumValue("mcp")] Mcp,
    [EnumValue("validation")] Validation,
    [EnumValue("resource")] Resource,
    [EnumValue("scheduling")] Scheduling,
    [EnumValue("general")] General
}
