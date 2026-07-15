
using JoinCode.Abstractions.Attributes;

namespace Core.DependencyInjection;

[Register]
public sealed partial class McpToolSyncBridge
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IChatContextManager _contextManager;
    [Inject] private readonly ILogger<McpToolSyncBridge>? _logger;

    public McpToolSyncBridge(
        IToolRegistry toolRegistry,
        IChatContextManager contextManager,
        ILogger<McpToolSyncBridge>? logger = null)
    {
        _toolRegistry = toolRegistry;
        _contextManager = contextManager;
        _logger = logger;
    }

    public async Task OnToolsListChangedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var allToolInfos = await _toolRegistry.GetAllToolInfosAsync(cancellationToken).ConfigureAwait(false);
            var toolSpecs = allToolInfos.Select(t => new ToolSpec(
                t.Name,
                t.Description,
                SerializeToolSchema(t.InputSchema)
            )).ToList();

            await _contextManager.UpdateToolSpecsAsync(toolSpecs, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("MCP 工具同步联动: 已更新 {Count} 个工具规格到 ChatContextManager", toolSpecs.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "MCP 工具同步联动失败");
        }
    }

    public async Task OnResourcesListChangedAsync(string clientId, OperationResult<IReadOnlyList<string>> syncResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(syncResult);

        try
        {
            if (syncResult.Success && syncResult.GetData().Count > 0)
            {
                var data = syncResult.GetData();
                var uris = string.Join(", ", data);
                var message = $"MCP 服务器 {clientId} 的资源列表已变更，当前资源: {uris}，请按需重新读取";
                await _contextManager.AddDynamicSystemMessageAsync(message, cancellationToken).ConfigureAwait(false);

                _logger?.LogInformation("MCP 资源同步联动: 已通知 {ClientId} 的 {Count} 个资源变更", clientId, data.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.McpResourceSyncFailedLog));
        }
    }

    public async Task OnPromptsListChangedAsync(string clientId, OperationResult<IReadOnlyList<string>> syncResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(syncResult);

        try
        {
            if (syncResult.Success && syncResult.GetData().Count > 0)
            {
                var data = syncResult.GetData();
                var names = string.Join(", ", data);
                var message = L.T(StringKey.McpPromptSyncMessage, clientId, names);
                await _contextManager.AddDynamicSystemMessageAsync(message, cancellationToken).ConfigureAwait(false);

                _logger?.LogInformation(L.T(StringKey.McpPromptSyncUpdatedLog, clientId, data.Count));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "MCP 提示模板同步联动失败");
        }
    }

    private static string? SerializeToolSchema(ToolSchema? schema)
    {
        if (schema == null) return null;

        var props = string.Join(",", schema.Properties.Select(kvp =>
        {
            var desc = kvp.Value.Description != null
                ? ",\"description\":\"" + kvp.Value.Description.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
                : "";
            return "\"" + kvp.Key + "\":{\"type\":\"" + kvp.Value.Type + "\"" + desc + "}";
        }));

        var result = "{\"type\":\"" + schema.Type + "\",\"properties\":{" + props + "}";

        if (schema.Required is { Count: > 0 })
        {
            var req = string.Join(",", schema.Required.Select(r => "\"" + r + "\""));
            result += ",\"required\":[" + req + "]";
        }

        result += "}";
        return result;
    }
}
