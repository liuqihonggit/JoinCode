namespace JoinCode.Abstractions.Tools;

public interface IToolRegistry : IDisposable
{
    Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default);

    Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default);

    Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default);

    Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default);

    Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null);

    Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default);

    Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
