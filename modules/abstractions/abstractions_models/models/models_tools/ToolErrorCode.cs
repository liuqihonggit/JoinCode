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

/// <summary>
/// 工具错误码判定 — 把「哪些码算权限拒绝」收敛到一处（TASK125）。
/// 消费方（CLI 的 <c>permission_denials</c> 收集、GUI 拒绝卡片等）一律调这里，
/// 禁止各自散落 "PD002" 字面量或对结果文本做关键词匹配。
/// </summary>
public static class ToolErrorCodes {
    /// <summary>
    /// 是否属于权限拒绝。收录全部 <c>DeniedBy*</c>：
    /// 用户/策略/代理限制/远端策略/特性开关/hook 拒绝都算；
    /// <c>PR002 PermissionRequiredPendingConfirmation</c> 是「待确认」而非拒绝，不收录。
    /// </summary>
    public static bool IsPermissionDenial(string? errorCode) =>
        errorCode is not null && DenialCodes.Contains(errorCode);

    private static readonly FrozenSet<string> DenialCodes = new[] {
        ToolErrorCode.PermissionDeniedByUser,
        ToolErrorCode.PermissionDeniedByPolicy,
        ToolErrorCode.PermissionDeniedByAgentRestriction,
        ToolErrorCode.PermissionDeniedByRemotePolicy,
        ToolErrorCode.PermissionDeniedByFeatureFlag,
        ToolErrorCode.PermissionDeniedByHook,
    }.Select(code => code.ToValue()).ToFrozenSet(StringComparer.Ordinal);
}
