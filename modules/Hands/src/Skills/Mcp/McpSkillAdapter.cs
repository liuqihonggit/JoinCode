
namespace Core.Skills.Mcp;

public sealed partial class McpSkillAdapter
{
    private readonly IMcpClient _client;
    [Inject] private readonly ILogger<McpSkillAdapter>? _logger;

    public McpSkillAdapter(IMcpClient client, ILogger<McpSkillAdapter>? logger = null)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<SkillDefinition?> AdaptToolAsync(JoinCode.Abstractions.Tools.ToolInfo toolInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolInfo);

        try
        {
            var parameters = ConvertSchemaToParameters(toolInfo.InputSchema, toolInfo.InputSchema.Required);
            var steps = new List<SkillStep>
            {
                new()
                {
                    Id = "call_tool",
                    Type = SkillStepType.Tool,
                    Tool = $"mcp_{toolInfo.Name}",
                    Description = toolInfo.Description ?? $"调用 MCP 工具: {toolInfo.Name}",
                    Prompt = BuildToolPrompt(toolInfo),
                    Next = "format_result"
                },
                new()
                {
                    Id = "format_result",
                    Type = SkillStepType.Prompt,
                    Description = "格式化工具调用结果",
                    Prompt = "格式化并展示 MCP 工具调用的结果"
                }
            };

            return new SkillDefinition
            {
                Name = $"mcp_{toolInfo.Name}",
                Description = $"[MCP] {toolInfo.Description ?? toolInfo.Name}",
                Version = "1.0",
                Parameters = parameters,
                Steps = steps,
                RequiresConfirmation = toolInfo.Annotations?.DestructiveHint == true,
                Tags = new List<string> { "mcp", "tool" }.AsReadOnly(),
                Permissions = new List<string> { "mcp.tool.call" }.AsReadOnly(),
                Namespace = "mcp"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[McpSkillAdapter] 适配工具 {ToolName} 失败", toolInfo.Name);
            return null;
        }
    }

    public async Task<SkillResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, JsonElement>? arguments,
        ExecutionContext ctx,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        var actualToolName = toolName.StartsWith("mcp_") ? toolName[4..] : toolName;

        try
        {
            var result = await _client.CallToolAsync(actualToolName, arguments, cancellationToken).ConfigureAwait(false);

            if (result.IsError)
            {
                var errorContent = result.GetFirstText() ?? "未知错误";
                return SkillResult.FailureResult(toolName, $"MCP 工具 '{actualToolName}' 执行失败: {errorContent}");
            }

            var output = result.GetFirstText() ?? string.Empty;
            return SkillResult.SuccessResult(toolName, output);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[McpSkillAdapter] 执行 MCP 工具 {ToolName} 失败", actualToolName);
            return SkillResult.FailureResult(toolName, ex.Message);
        }
    }

    private static Dictionary<string, SkillParameter> ConvertSchemaToParameters(
        ToolSchema schema,
        IReadOnlyList<string>? requiredParams)
    {
        var parameters = new Dictionary<string, SkillParameter>();

        foreach (var (name, prop) in schema.Properties)
        {
            var isRequired = requiredParams?.Contains(name) ?? false;
            parameters[name] = new SkillParameter
            {
                Type = MapJsonTypeToSkillType(prop.Type),
                Description = prop.Description ?? name,
                Required = isRequired,
                DefaultValue = prop.Default,
                Validation = BuildValidation(prop)
            };
        }

        return parameters;
    }

    private static string MapJsonTypeToSkillType(string jsonType)
    {
        return jsonType.ToLowerInvariant() switch
        {
            "string" => "string",
            "integer" => "integer",
            "number" => "number",
            "boolean" => "boolean",
            "array" => "array",
            "object" => "object",
            _ => "string"
        };
    }

    private static ParameterValidation? BuildValidation(ToolSchemaProperty prop)
    {
        if (prop.Enum != null && prop.Enum.Count > 0)
        {
            return new ParameterValidation { EnumValues = prop.Enum };
        }

        return null;
    }

    private static string BuildToolPrompt(JoinCode.Abstractions.Tools.ToolInfo toolInfo)
    {
        var paramList = toolInfo.InputSchema.Properties.Keys;
        return $"调用 MCP 工具 {toolInfo.Name}，参数: {string.Join(", ", paramList)}";
    }
}
