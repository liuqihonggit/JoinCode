namespace JoinCode.CliCommands;

/// <summary>
/// jcc 专属 MCP 服务端 — 继承 McpServer，接入 IMcpToolRegistry，
/// 把全部内部工具（含 gh_*、tool_search、read、write 等）暴露为 MCP 协议 tools/list + tools/call。
/// <para>tools/call 执行统一走 <see cref="IToolExecutionGateway"/> 权限管道（13 层 PermissionCheck/Path/AgentRestriction 等），
/// 禁止直连注册表绕过权限层 — ADR: 0065（mcp serve）、0138（serve token 鉴权 + 权限管道接入）</para>
/// </summary>
public sealed class JccMcpServer : McpServer {
    private readonly IMcpToolRegistry _registry;
    private readonly IToolExecutionGateway _gateway;

    /// <summary>
    /// 构造函数 — 注入工具注册表、权限执行器并初始化 MCP 服务端
    /// </summary>
    /// <param name="registry">MCP 工具注册表（tools/list 与 containsTool 判断用）</param>
    /// <param name="gateway">权限感知工具执行器（tools/call 执行统一入口，禁止绕过权限管道）</param>
    /// <param name="serverName">服务端名称，默认 jcc-mcp</param>
    /// <param name="serverVersion">服务端版本，为 null 时使用默认值</param>
    /// <param name="instructions">服务端说明信息</param>
    public JccMcpServer(IMcpToolRegistry registry, IToolExecutionGateway gateway, string serverName = "jcc-mcp", string? serverVersion = null, string? instructions = null)
        : base(serverName, serverVersion, instructions) {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    /// <summary>
    /// 处理 tools/list 请求 — 从注册表枚举全部工具并转换为 MCP 协议定义
    /// </summary>
    /// <returns>工具列表结果</returns>
    protected override async Task<ListToolsResult> HandleListToolsAsync() {
        var tools = await _registry.GetAllToolsAsync(CancellationToken.None).ConfigureAwait(false);
        var list = new List<JoinCode.Abstractions.Mcp.Protocol.ToolDefinition>(tools.Count);
        foreach (var kv in tools) {
            var handler = kv.Value;
            list.Add(new JoinCode.Abstractions.Mcp.Protocol.ToolDefinition {
                Name = handler.Name,
                Description = handler.Description,
                InputSchema = JsonSerializer.SerializeToElement(handler.InputSchema, ContractsJsonContext.Default.ToolSchema),
                Category = handler.Category ?? "general"
            });
        }
        return new ListToolsResult { Tools = list };
    }

    /// <summary>
    /// 处理 tools/call 请求 — 根据工具名查找并执行对应处理器，返回执行结果内容
    /// </summary>
    /// <param name="paramsObj">调用参数 JSON 元素</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具调用结果</returns>
    protected override async Task<CallToolResult> HandleCallToolAsync(JsonElement? paramsObj, CancellationToken cancellationToken) {
        if (paramsObj is null)
            return new CallToolResult {
                Content = [new McpToolContent { Text = "缺少 tools/call 参数" }],
                IsError = true
            };

        var callParams = McpJsonSerializer.DeserializeCallToolRequestParams(paramsObj.Value.GetRawText());
        if (callParams is null || string.IsNullOrEmpty(callParams.Name))
            return new CallToolResult {
                Content = [new McpToolContent { Text = "tools/call 缺少 name 字段" }],
                IsError = true
            };

        if (!await _registry.ContainsToolAsync(callParams.Name, cancellationToken).ConfigureAwait(false))
            return new CallToolResult {
                Content = [new McpToolContent { Text = $"Tool not found: {callParams.Name}" }],
                IsError = true
            };

        var arguments = ParseArguments(callParams.Arguments);
        // 执行统一走权限管道（PermissionAwareToolExecutor → 13 层中间件），禁止直连注册表绕过权限层
        var result = await _gateway.ExecuteAsync(callParams.Name, arguments, cancellationToken).ConfigureAwait(false);

        var contents = new List<McpToolContent>(result.Content.Count);
        foreach (var c in result.Content) {
            contents.Add(new McpToolContent {
                Type = c.Type.ToValue(),
                Text = c.Text,
                Data = c.Data,
                MimeType = c.MimeType
            });
        }
        return new CallToolResult { Content = contents, IsError = result.IsError };
    }

    private static Dictionary<string, JsonElement> ParseArguments(JsonElement? arguments) {
        if (arguments is null || arguments.Value.ValueKind != JsonValueKind.Object)
            return new(StringComparer.Ordinal);

        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in arguments.Value.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }
}