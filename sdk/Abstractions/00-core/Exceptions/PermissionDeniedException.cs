
namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// 权限拒绝异常
/// </summary>
public sealed class PermissionDeniedException : WorkflowException
{
    /// <summary>
    /// 被拒绝的资源类型
    /// </summary>
    public PermissionResourceType ResourceType { get; }

    /// <summary>
    /// 被拒绝的资源名称
    /// </summary>
    public string ResourceName { get; }

    /// <summary>
    /// 请求的主体（Agent 或用户）
    /// </summary>
    public string? Principal { get; }

    /// <summary>
    /// 拒绝原因
    /// </summary>
    public string DenyReason { get; }

    /// <summary>
    /// 请求参数
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Parameters { get; }

    /// <inheritdoc />
    public override bool IsRetryable => false;

    /// <summary>
    /// 创建 PermissionDeniedException
    /// </summary>
    public PermissionDeniedException(
        PermissionResourceType resourceType,
        string resourceName,
        string denyReason,
        string? principal = null,
        IReadOnlyDictionary<string, JsonElement>? parameters = null,
        string? errorCode = null,
        ExceptionContext? context = null)
        : base(
            $"权限被拒绝: {GetResourceTypeName(resourceType)} '{resourceName}' 无法访问。原因: {denyReason}",
            errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.PermissionDenied.ToValue(),
            ErrorCategory.Permission,
            context)
    {
        ResourceType = resourceType;
        ResourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
        DenyReason = denyReason ?? throw new ArgumentNullException(nameof(denyReason));
        Principal = principal;
        Parameters = parameters;
    }

    /// <summary>
    /// 创建 PermissionDeniedException（带内部异常）
    /// </summary>
    public PermissionDeniedException(
        PermissionResourceType resourceType,
        string resourceName,
        string denyReason,
        Exception innerException,
        string? principal = null,
        IReadOnlyDictionary<string, JsonElement>? parameters = null,
        string? errorCode = null,
        ExceptionContext? context = null)
        : base(
            $"权限被拒绝: {GetResourceTypeName(resourceType)} '{resourceName}' 无法访问。原因: {denyReason}",
            innerException,
            errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.PermissionDenied.ToValue(),
            ErrorCategory.Permission,
            context)
    {
        ResourceType = resourceType;
        ResourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
        DenyReason = denyReason ?? throw new ArgumentNullException(nameof(denyReason));
        Principal = principal;
        Parameters = parameters;
    }

    /// <summary>
    /// 创建工具权限拒绝异常
    /// </summary>
    public static PermissionDeniedException ToolDenied(
        string toolName,
        string denyReason,
        string? agentName = null,
        IReadOnlyDictionary<string, JsonElement>? parameters = null)
    {
        return new PermissionDeniedException(
            PermissionResourceType.Tool,
            toolName,
            denyReason,
            agentName,
            parameters,
            global::JoinCode.Abstractions.Exceptions.ErrorCode.PermissionToolDenied.ToValue());
    }

    /// <summary>
    /// 创建路径权限拒绝异常
    /// </summary>
    public static PermissionDeniedException PathDenied(
        string path,
        string denyReason,
        string? agentName = null)
    {
        return new PermissionDeniedException(
            PermissionResourceType.Path,
            path,
            denyReason,
            agentName,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.PermissionPathDenied.ToValue());
    }

    /// <summary>
    /// 创建无效权限模式异常
    /// </summary>
    public static PermissionDeniedException InvalidMode(string mode, string reason)
    {
        return new PermissionDeniedException(
            PermissionResourceType.PermissionMode,
            mode,
            reason,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.PermissionInvalidMode.ToValue());
    }

    private static string GetResourceTypeName(PermissionResourceType type) => type switch
    {
        PermissionResourceType.Tool => "工具",
        PermissionResourceType.Path => "路径",
        PermissionResourceType.PermissionMode => "权限模式",
        PermissionResourceType.Operation => "操作",
        _ => "资源"
    };
}

/// <summary>
/// 权限资源类型
/// </summary>
public enum PermissionResourceType
{
    Tool,
    Path,
    PermissionMode,
    Operation
}
