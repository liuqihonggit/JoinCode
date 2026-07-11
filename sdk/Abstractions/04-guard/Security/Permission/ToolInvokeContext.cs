namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 工具调用上下文
/// </summary>
public sealed partial class ToolInvokeContext
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
    /// 请求ID
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// 调用时间
    /// </summary>
    public DateTimeOffset InvokeTime { get; }

    /// <summary>
    /// 创建工具调用上下文
    /// </summary>
    public ToolInvokeContext(string toolName, Dictionary<string, JsonElement>? arguments = null)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Arguments = arguments;
        RequestId = Guid.NewGuid().ToString("N");
        InvokeTime = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 创建工具调用上下文（指定请求ID）
    /// </summary>
    public ToolInvokeContext(string toolName, Dictionary<string, JsonElement>? arguments, string requestId)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Arguments = arguments;
        RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
        InvokeTime = DateTimeOffset.UtcNow;
    }
}
