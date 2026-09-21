namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 执行侧工具注册表 — 异步操作，含注册/查询/执行能力
/// 关系: IToolCollection (01-ai) 是本接口的 LLM 侧只读投影
/// </summary>
public interface IToolRegistry : IAsyncDisposable, IRegistry {
    /// <summary>异步注册工具处理器。</summary>
    Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default);

    /// <summary>异步注册工具（按名称、描述、输入模式和处理委托）。</summary>
    Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default, ToolKind kind = ToolKind.System, string? groupName = null, ToolTimeoutPolicy? timeoutPolicy = null, string? category = null);

    /// <summary>异步注销工具。</summary>
    Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default);

    /// <summary>异步获取工具处理器。</summary>
    Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default);

    /// <summary>异步获取全部工具。</summary>
    Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>异步执行工具。</summary>
    Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null);

    /// <summary>异步获取工具信息。</summary>
    Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default);

    /// <summary>异步获取全部工具信息。</summary>
    Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default);

    /// <summary>异步判断是否包含指定工具。</summary>
    Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default);

    /// <summary>异步获取工具数量。</summary>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>异步清空所有工具。</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>异步获取分组名称集合。</summary>
    Task<FrozenSet<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>异步按类型获取工具。</summary>
    Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByKindAsync(ToolKind kind, CancellationToken cancellationToken = default);

    /// <summary>异步按分组获取工具。</summary>
    Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByGroupAsync(string groupName, CancellationToken cancellationToken = default);
}