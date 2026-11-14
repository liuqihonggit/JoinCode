namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具错误码枚举 — 精确标识每个错误场景，方便 AI 和日志系统精确匹配。
/// 格式：前缀_编号，前缀对应 ToolErrorType 的缩写，编号区分同一类型下的不同子场景。
/// </summary>
public enum ToolErrorCode {
    // === parameter_missing (PM) ===
    [EnumValue("PM001")] ParameterMissingRequired,
    [EnumValue("PM002")] ParameterMissingOptionalButRequired,

    // === schema_validation (SV) ===
    [EnumValue("SV001")] SchemaValidationTypeMismatch,
    [EnumValue("SV002")] SchemaValidationOutOfRange,
    [EnumValue("SV003")] SchemaValidationUnknownField,
    [EnumValue("SV004")] SchemaValidationFormatError,

    // === permission_denied (PD) ===
    [EnumValue("PD001")] PermissionDeniedByPolicy,
    [EnumValue("PD002")] PermissionDeniedByUser,
    [EnumValue("PD00B")] PermissionDeniedByAgentRestriction,
    [EnumValue("PD003")] PermissionDeniedByRemotePolicy,
    [EnumValue("PD004")] PermissionDeniedByFeatureFlag,
    [EnumValue("PD005")] PermissionDeniedByHook,

    // === permission_required (PR) ===
    [EnumValue("PR001")] PermissionRequiredNoHandler,
    [EnumValue("PR002")] PermissionRequiredPendingConfirmation,

    // === tool_not_found (TNF) ===
    [EnumValue("TNF001")] ToolNotFoundInRegistry,
    [EnumValue("TNF002")] HandlerNotFoundInContext,

    // === execution_failed (EF) ===
    [EnumValue("EF001")] ExecutionFailedException,
    [EnumValue("EF002")] ExecutionFailedTimeout,
    [EnumValue("EF003")] ExecutionFailedNoResult,
    [EnumValue("EF004")] ExecutionFailedGatewayUnavailable,

    // === cancelled (CNL) ===
    [EnumValue("CNL001")] CancelledByUser,
    [EnumValue("CNL002")] CancelledBySiblingError,
    [EnumValue("CNL003")] CancelledByTimeout,
}
