namespace JoinCode.CliCommands;

/// <summary>
/// jcc 专属 MCP 服务端 — 继承 McpServer，接入 IMcpToolRegistry，
/// 把全部内部工具（含 gh_*、tool_search、read、write 等）暴露为 MCP 协议 tools/list + tools/call。
/// <para>ADR: 0065 — jcc mcp serve 子命令使用此类启动 MCP 服务端</para>
/// </summary>
public sealed class JccMcpServer : McpServer
{
    private readonly IMcpToolRegistry _registry;

    public JccMcpServer(IMcpToolRegistry registry, string serverName = "jcc-mcp", string? serverVersion = null, string? instructions = null)
        : base(serverName, serverVersion, instructions)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    protected override ListToolsResult HandleListTools()
    {
        var tools = _registry.GetAllToolsAsync(CancellationToken.None).GetAwaiter().GetResult();
        var list = new List<JoinCode.Abstractions.Mcp.Protocol.ToolDefinition>(tools.Count);
        foreach (var kv in tools)
        {
            var handler = kv.Value;
            list.Add(new JoinCode.Abstractions.Mcp.Protocol.ToolDefinition
            {
                Name = handler.Name,
                Description = handler.Description,
                InputSchema = JsonSerializer.SerializeToElement(handler.InputSchema, ContractsJsonContext.Default.ToolSchema),
                Category = handler.Category ?? "general"
            });
        }
        return new ListToolsResult { Tools = list };
    }

    protected override async Task<CallToolResult> HandleCallToolAsync(JsonElement? paramsObj, CancellationToken cancellationToken)
    {
        if (paramsObj is null)
            return new CallToolResult
            {
                Content = [new McpToolContent { Text = "缺少 tools/call 参数" }],
                IsError = true
            };

        var callParams = McpJsonSerializer.DeserializeCallToolRequestParams(paramsObj.Value.GetRawText());
        if (callParams is null || string.IsNullOrEmpty(callParams.Name))
            return new CallToolResult
            {
                Content = [new McpToolContent { Text = "tools/call 缺少 name 字段" }],
                IsError = true
            };

        if (!await _registry.ContainsToolAsync(callParams.Name, cancellationToken).ConfigureAwait(false))
            return new CallToolResult
            {
                Content = [new McpToolContent { Text = $"Tool not found: {callParams.Name}" }],
                IsError = true
            };

        var arguments = ParseArguments(callParams.Arguments);
        var result = await _registry.ExecuteToolAsync(callParams.Name, arguments, cancellationToken).ConfigureAwait(false);

        var contents = new List<McpToolContent>(result.Content.Count);
        foreach (var c in result.Content)
        {
            contents.Add(new McpToolContent
            {
                Type = c.Type.ToValue(),
                Text = c.Text,
                Data = c.Data,
                MimeType = c.MimeType
            });
        }
        return new CallToolResult { Content = contents, IsError = result.IsError };
    }

    private static Dictionary<string, JsonElement> ParseArguments(JsonElement? arguments)
    {
        if (arguments is null || arguments.Value.ValueKind != JsonValueKind.Object)
            return new(StringComparer.Ordinal);

        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in arguments.Value.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }
}
