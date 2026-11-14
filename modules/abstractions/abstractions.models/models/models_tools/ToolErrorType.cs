namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具错误类型枚举 — 覆盖工具执行管道中所有可能的错误分类。
/// 每个类型对应管道中一个特定的失败点，AI 可据此判断错误性质和后续操作。
/// </summary>
public enum ToolErrorType {
    /// <summary>必填参数缺失 — RequiredParamsMiddleware (Order=200) 拦截</summary>
    [EnumValue("parameter_missing")] ParameterMissing,

    /// <summary>参数 Schema 校验失败 — SchemaValidationMiddleware (Order=300) 拦截</summary>
    [EnumValue("schema_validation")] SchemaValidation,

    /// <summary>权限被拒绝 — PermissionCheckMiddleware (Order=500) 拦截</summary>
    [EnumValue("permission_denied")] PermissionDenied,

    /// <summary>权限需要确认 — PermissionCheckMiddleware (Order=500) 拦截，等待用户确认</summary>
    [EnumValue("permission_required")] PermissionRequired,

    /// <summary>工具未找到 — PermissionAwareToolExecutor 或 ToolExecutionMiddleware 拦截</summary>
    [EnumValue("tool_not_found")] ToolNotFound,

    /// <summary>工具执行失败 — ToolExecutionMiddleware (Order=900) catch Exception</summary>
    [EnumValue("execution_failed")] ExecutionFailed,

    /// <summary>工具执行超时 — OneShotCommandGroup 超时策略触发</summary>
    [EnumValue("timeout")] Timeout,

    /// <summary>工具执行被取消 — OperationCanceledException</summary>
    [EnumValue("cancelled")] Cancelled,

    /// <summary>Agent 模式限制 — AgentRestrictionMiddleware (Order=400) 拦截</summary>
    [EnumValue("agent_restricted")] AgentRestricted,

    /// <summary>FeatureFlag 禁用 — FeatureFlagMiddleware (Order=700) 拦截</summary>
    [EnumValue("feature_flag_disabled")] FeatureFlagDisabled,

    /// <summary>远程策略不允许 — RemotePolicyMiddleware (Order=600) 拦截</summary>
    [EnumValue("remote_policy")] RemotePolicy,

    /// <summary>PreToolUse Hook 阻断 — PreToolUseHookMiddleware (Order=800) 拦截</summary>
    [EnumValue("hook_blocked")] HookBlocked,

    /// <summary>工具执行网关不可用 — ChatToolOrchestrator 检测</summary>
    [EnumValue("gateway_unavailable")] GatewayUnavailable,

    /// <summary>管道完成但无结果 — PermissionAwareToolExecutor 检测</summary>
    [EnumValue("no_result")] NoResult,

    /// <summary>Handler 未找到 — ToolExecutionMiddleware 检测</summary>
    [EnumValue("handler_not_found")] HandlerNotFound,
}
