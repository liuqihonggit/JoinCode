
namespace McpBridge;

public sealed class McpToolBridge
{
    private readonly IToolRegistry _toolRegistry;

    public McpToolBridge(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
    }

    /// <summary>
    /// 从 IToolRegistry 提取所有工具，按 ToolKind 分组为 core_tools（System）和 mcp_tools（Mcp/Slash）
    /// 两阶段工具加载：核心工具发完整 schema，MCP 工具首次只发分组+名称
    /// </summary>
    public async Task<IReadOnlyList<IToolGroup>> CreatePluginAsync(CancellationToken cancellationToken = default)
    {
        var allTools = await _toolRegistry.GetAllToolsAsync(cancellationToken);

        var visibleHandlers = allTools.Values
            .Where(h => h.Kind != ToolKind.OnError)
            .ToList();

        var coreFunctions = new List<IToolDef>();
        var mcpFunctions = new List<IToolDef>();

        foreach (var h in visibleHandlers)
        {
            var toolInfo = new ToolInfo
            {
                Name = h.Name,
                Description = h.Description,
                InputSchema = h.InputSchema
            };
            var toolDef = new ToolDef(
                h.Name,
                h.Description ?? string.Empty,
                BuildParameters(toolInfo));

            if (h.Kind == ToolKind.System)
                coreFunctions.Add(toolDef);
            else
                mcpFunctions.Add(toolDef);
        }

        var groups = new List<IToolGroup>();
        if (coreFunctions.Count > 0)
            groups.Add(new ToolGroup(ToolGroupNameConstants.CoreTools, coreFunctions));
        if (mcpFunctions.Count > 0)
            groups.Add(new ToolGroup(ToolGroupNameConstants.McpTools, mcpFunctions));
        return groups;
    }

    private static IReadOnlyList<IToolParam> BuildParameters(ToolInfo toolInfo)
    {
        var requiredSet = (toolInfo.InputSchema.Required ?? []).ToFrozenSet();

        return toolInfo.InputSchema.Properties
            .Select(kvp =>
            {
                var description = kvp.Value.Description ?? string.Empty;
                if (kvp.Value.Enum is { Count: > 0 })
                {
                    description = string.IsNullOrEmpty(description)
                        ? $"Allowed values: {string.Join(", ", kvp.Value.Enum)}"
                        : $"{description} Allowed values: {string.Join(", ", kvp.Value.Enum)}";
                }

                return new ToolParam(
                    kvp.Key,
                    description,
                    MapSchemaTypeToClrType(kvp.Value.Type),
                    requiredSet.Contains(kvp.Key));
            })
            .ToList();
    }

    private static Type MapSchemaTypeToClrType(string schemaType)
    {
        return schemaType switch
        {
            "string" => typeof(string),
            "integer" => typeof(int),
            "number" => typeof(double),
            "boolean" => typeof(bool),
            "array" => typeof(string),
            "object" => typeof(string),
            _ => typeof(string)
        };
    }
}
