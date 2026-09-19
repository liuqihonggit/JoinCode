
namespace Core.DependencyInjection;

/// <summary>
/// MCP 工具同步桥 — 监听远程 MCP 客户端的工具/资源/提示列表变更事件，同步到 <see cref="IChatContextManager"/>。
/// <para>当远程 MCP 服务器工具列表变更时，更新 ChatContextManager 的工具规格；</para>
/// <para>当资源/提示列表变更时，向上下文注入动态系统消息通知 LLM。</para>
/// </summary>
[Register(typeof(McpToolSyncBridge), ServiceLifetime.Singleton)]
public sealed partial class McpToolSyncBridge : ServiceEntity {
    private readonly IToolRegistry _toolRegistry;
    private readonly IChatContextManager _contextManager;
    private readonly ILogger<McpToolSyncBridge>? _logger;

    /// <summary>
    /// 初始化 <see cref="McpToolSyncBridge"/> 实例。
    /// </summary>
    /// <param name="toolRegistry">工具注册表（获取所有工具信息）。</param>
    /// <param name="contextManager">聊天上下文管理器（更新工具规格和动态系统消息）。</param>
    /// <param name="logger">可选的日志记录器。</param>
    public McpToolSyncBridge(
        IToolRegistry toolRegistry,
        IChatContextManager contextManager,
        ILogger<McpToolSyncBridge>? logger = null) {
        _toolRegistry = toolRegistry;
        _contextManager = contextManager;
        _logger = logger;
    }

    /// <summary>
    /// 工具列表变更回调：拉取所有工具信息，转换为 <see cref="ToolSpec"/> 后更新到 <see cref="IChatContextManager"/>。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task OnToolsListChangedAsync(CancellationToken cancellationToken = default) {
        try {
            var allToolInfos = await _toolRegistry.GetAllToolInfosAsync(cancellationToken).ConfigureAwait(false);
            var toolSpecs = allToolInfos.Select(t => new ToolSpec(
                t.Name,
                t.Description,
                SerializeToolSchema(t.InputSchema),
                t.Category,
                t.GroupName
            )).ToList();

            await _contextManager.UpdateToolSpecsAsync(toolSpecs, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("MCP 工具同步联动: 已更新 {Count} 个工具规格到 ChatContextManager", toolSpecs.Count);
        } catch (Exception ex) {
            _logger?.LogError(ex, "MCP 工具同步联动失败");
        }
    }

    /// <summary>
    /// 资源列表变更回调：当指定 MCP 客户端的资源列表变更且非空时，向上下文注入动态系统消息通知 LLM 重新读取。
    /// </summary>
    /// <param name="clientId">MCP 客户端标识。</param>
    /// <param name="syncResult">同步结果（含当前资源 URI 列表）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task OnResourcesListChangedAsync(string clientId, OperationResult<IReadOnlyList<string>> syncResult, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(syncResult);

        try {
            if (syncResult.Success && syncResult.GetData().Count > 0) {
                var data = syncResult.GetData();
                var uris = string.Join(", ", data);
                var message = $"MCP 服务器 {clientId} 的资源列表已变更，当前资源: {uris}，请按需重新读取";
                await _contextManager.AddDynamicSystemMessageAsync(message, cancellationToken).ConfigureAwait(false);

                _logger?.LogInformation("MCP 资源同步联动: 已通知 {ClientId} 的 {Count} 个资源变更", clientId, data.Count);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, L.T(StringKey.McpResourceSyncFailedLog));
        }
    }

    /// <summary>
    /// 提示模板列表变更回调：当指定 MCP 客户端的提示模板列表变更且非空时，向上下文注入动态系统消息通知 LLM。
    /// </summary>
    /// <param name="clientId">MCP 客户端标识。</param>
    /// <param name="syncResult">同步结果（含当前提示模板名称列表）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task OnPromptsListChangedAsync(string clientId, OperationResult<IReadOnlyList<string>> syncResult, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(syncResult);

        try {
            if (syncResult.Success && syncResult.GetData().Count > 0) {
                var data = syncResult.GetData();
                var names = string.Join(", ", data);
                var message = L.T(StringKey.McpPromptSyncMessage, clientId, names);
                await _contextManager.AddDynamicSystemMessageAsync(message, cancellationToken).ConfigureAwait(false);

                _logger?.LogInformation(L.T(StringKey.McpPromptSyncUpdatedLog, clientId, data.Count));
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "MCP 提示模板同步联动失败");
        }
    }

    private static string? SerializeToolSchema(ToolSchema? schema) {
        if (schema == null) return null;

        var props = string.Join(",", schema.Properties.Select(kvp => {
            var desc = kvp.Value.Description != null
                ? ",\"description\":\"" + kvp.Value.Description.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
                : "";
            return "\"" + kvp.Key + "\":{\"type\":\"" + kvp.Value.Type + "\"" + desc + "}";
        }));

        var result = "{\"type\":\"" + schema.Type + "\",\"properties\":{" + props + "}";

        if (schema.Required is { Count: > 0 }) {
            var req = string.Join(",", schema.Required.Select(r => "\"" + r + "\""));
            result += ",\"required\":[" + req + "]";
        }

        result += "}";
        return result;
    }
}