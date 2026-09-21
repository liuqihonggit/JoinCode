
namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 委托工具处理器 - 使用委托实现工具处理
/// </summary>
public sealed class DelegateToolHandler : IToolHandler {
    private readonly ToolHandler _handler;

    /// <summary>获取工具名称。</summary>
    public string Name { get; }
    /// <summary>获取工具描述。</summary>
    public string Description { get; }
    /// <summary>获取输入参数模式。</summary>
    public ToolSchema InputSchema { get; }
    /// <summary>获取工具种类。</summary>
    public ToolKind Kind { get; }
    /// <summary>获取工具组名。</summary>
    public string? GroupName { get; }
    /// <summary>获取工具分类。</summary>
    public string? Category { get; }
    /// <summary>获取超时策略。</summary>
    public ToolTimeoutPolicy TimeoutPolicy { get; }

    /// <summary>构造委托工具处理器。</summary>
    /// <param name="name">工具名称。</param>
    /// <param name="description">工具描述。</param>
    /// <param name="inputSchema">输入参数模式。</param>
    /// <param name="handler">工具处理委托。</param>
    /// <param name="kind">工具种类。</param>
    /// <param name="groupName">工具组名。</param>
    /// <param name="timeoutPolicy">超时策略。</param>
    /// <param name="category">工具分类。</param>
    public DelegateToolHandler(string name, string description, ToolSchema inputSchema, ToolHandler handler, ToolKind kind = ToolKind.System, string? groupName = null, ToolTimeoutPolicy? timeoutPolicy = null, string? category = null) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(handler);

        Name = name;
        Description = description;
        InputSchema = inputSchema;
        Kind = kind;
        GroupName = groupName;
        Category = category;
        TimeoutPolicy = timeoutPolicy ?? ToolTimeoutPolicy.None;
        _handler = handler;
    }

    /// <summary>执行工具。</summary>
    /// <param name="arguments">调用参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="onProgress">进度回调。</param>
    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null) {
        return _handler(Name, arguments, cancellationToken, onProgress);
    }
}
