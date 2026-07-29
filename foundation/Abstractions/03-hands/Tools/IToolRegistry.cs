namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 执行侧工具注册表 — 异步操作，含注册/查询/执行能力
/// 关系: IToolCollection (01-ai) 是本接口的 LLM 侧只读投影
/// </summary>
public interface IToolRegistry : IAsyncDisposable
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
