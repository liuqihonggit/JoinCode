namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// 标准错误码枚举 — 替代原 ErrorCodes 静态常量类
/// </summary>
public enum ErrorCode
{
    // 工作流相关 (WF)
    [EnumValue("WF001")] WorkflowGeneral,
    [EnumValue("WF002")] WorkflowInitialization,
    [EnumValue("WF003")] WorkflowExecution,
    [EnumValue("WF004")] WorkflowCancelled,

    // 配置相关 (CFG)
    [EnumValue("CFG001")] ConfigurationGeneral,
    [EnumValue("CFG002")] ConfigurationMissing,
    [EnumValue("CFG003")] ConfigurationInvalid,
    [EnumValue("CFG004")] ConfigurationParseError,

    // API 相关 (API)
    [EnumValue("API001")] ApiGeneral,
    [EnumValue("API002")] ApiConnection,
    [EnumValue("API003")] ApiTimeout,
    [EnumValue("API004")] ApiRateLimit,
    [EnumValue("API005")] ApiAuthentication,
    [EnumValue("API006")] ApiAuthorization,
    [EnumValue("API007")] ApiResponseError,
    [EnumValue("API008")] ApiServerError,
    [EnumValue("API009")] ApiValidation,

    // 代码执行相关 (CE)
    [EnumValue("CE001")] CodeExecutionGeneral,
    [EnumValue("CE002")] CodeExecutionTimeout,
    [EnumValue("CE003")] CodeExecutionCompilation,
    [EnumValue("CE004")] CodeExecutionRuntime,

    // 权限相关 (PERM)
    [EnumValue("PERM001")] PermissionDenied,
    [EnumValue("PERM002")] PermissionToolDenied,
    [EnumValue("PERM003")] PermissionPathDenied,
    [EnumValue("PERM004")] PermissionInvalidMode,

    // MCP 相关 (MCP)
    [EnumValue("MCP001")] McpGeneral,
    [EnumValue("MCP002")] McpProtocol,
    [EnumValue("MCP003")] McpConnection,
    [EnumValue("MCP004")] McpToolNotFound,
    [EnumValue("MCP005")] McpInstanceCreation,

    // 验证相关 (VAL)
    [EnumValue("VAL001")] ValidationGeneral,
    [EnumValue("VAL002")] ValidationRequired,
    [EnumValue("VAL003")] ValidationFormat,
    [EnumValue("VAL004")] ValidationRange,

    // 资源相关 (RES)
    [EnumValue("RES001")] ResourceNotFound,
    [EnumValue("RES002")] ResourceAlreadyExists,
    [EnumValue("RES003")] ResourceLocked,
    [EnumValue("RES004")] ResourceUnavailable,

    // 调度相关 (SCH)
    [EnumValue("SCH001")] SchedulingGeneral,
    [EnumValue("SCH002")] SchedulingConflict,
    [EnumValue("SCH003")] SchedulingTimeout,

    // 一般错误 (GEN)
    [EnumValue("GEN001")] General,
    [EnumValue("GEN002")] NotSupported,
    [EnumValue("GEN003")] NotImplemented,
    [EnumValue("GEN004")] OperationCancelled,
}
