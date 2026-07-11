namespace JoinCode.Abstractions.Security;

/// <summary>
/// 权限请求类，包含工具执行所需的权限信息
/// </summary>
public sealed class PermissionRequest
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// 工具参数
    /// </summary>
    public Dictionary<string, JsonElement>? Arguments { get; }

    /// <summary>
    /// 请求时间
    /// </summary>
    public DateTimeOffset RequestTime { get; }

    /// <summary>
    /// 请求ID
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// 创建权限请求
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    public PermissionRequest(string toolName, Dictionary<string, JsonElement>? arguments = null)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Arguments = arguments;
        RequestTime = DateTimeOffset.UtcNow;
        RequestId = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 创建权限请求（指定请求ID）
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    /// <param name="requestId">请求ID</param>
    /// <param name="requestTime">请求时间</param>
    public PermissionRequest(string toolName, Dictionary<string, JsonElement>? arguments, string requestId, DateTimeOffset requestTime)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Arguments = arguments;
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        RequestTime = requestTime;
    }
}
