
namespace Api.Mcp;

public sealed class McpToolBridge
{
    private readonly IToolRegistry _toolRegistry;

    public McpToolBridge(IToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
    }

    public async Task<IToolGroup> CreatePluginAsync(CancellationToken cancellationToken = default)
    {
        var tools = await _toolRegistry.GetAllToolInfosAsync(cancellationToken);
        var functions = tools.Select(t => (IToolDef)new ToolDef(
            t.Name,
            t.Description ?? string.Empty,
            BuildParameters(t))).ToList();

        return new ToolGroup("mcp_tools", functions);
    }

    private static IReadOnlyList<IToolParam> BuildParameters(ToolInfo toolInfo)
    {
        var required = toolInfo.InputSchema.Required ?? [];

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
                    required.Contains(kvp.Key));
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
