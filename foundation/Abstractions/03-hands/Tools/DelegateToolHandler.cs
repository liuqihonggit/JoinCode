
namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 委托工具处理器 - 使用委托实现工具处理
/// </summary>
public sealed class DelegateToolHandler : IToolHandler
{
    private readonly ToolHandler _handler;

    public string Name { get; }
    public string Description { get; }
    public ToolSchema InputSchema { get; }

    public DelegateToolHandler(string name, string description, ToolSchema inputSchema, ToolHandler handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(handler);

        Name = name;
        Description = description;
        InputSchema = inputSchema;
        _handler = handler;
    }

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        return _handler(Name, arguments, cancellationToken, onProgress);
    }
}
