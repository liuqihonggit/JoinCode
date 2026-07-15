namespace Mcp.MockServer.Engine;

using Mcp.MockServer.Models;

/// <summary>
/// MCP JSON-RPC 请求处理器 — 解析 JSON-RPC 消息并返回响应
/// 对齐 MCP 协议规范: https://spec.modelcontextprotocol.io/specification/2024-11-05/
/// </summary>
public sealed class McpMockServerEngine
{
    private readonly McpMockServerConfig _config;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, McpToolDefinition> _toolsByName;
    private string? _sessionId;
    private int _requestCount;
    private int _toolCallCount;

    public McpMockServerEngine(McpMockServerConfig config, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _toolsByName = new Dictionary<string, McpToolDefinition>(StringComparer.Ordinal);
        foreach (var tool in config.Tools)
        {
            if (!string.IsNullOrEmpty(tool.Name))
            {
                _toolsByName[tool.Name] = tool;
            }
        }
    }

    /// <summary>
    /// 处理 JSON-RPC 请求字符串，返回响应字符串（通知返回 null）
    /// </summary>
    public string? HandleRequest(string requestBody)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestBody);

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "[McpMock] JSON 解析失败");
            return BuildErrorResponse(null, -32700, "Parse error");
        }

        if (!root.TryGetProperty("jsonrpc", out var versionProp) || versionProp.GetString() != "2.0")
        {
            return BuildErrorResponse(GetId(root), -32600, "Invalid Request");
        }

        if (!root.TryGetProperty("method", out var methodProp))
        {
            return BuildErrorResponse(GetId(root), -32600, "Invalid Request");
        }

        var method = methodProp.GetString() ?? "";
        var id = GetId(root);

        // 通知（无 id）— 不返回响应
        if (id is null)
        {
            _logger?.LogDebug("[McpMock] 收到通知: {Method}", method);
            if (method == "notifications/initialized")
            {
                _logger?.LogInformation("[McpMock] 客户端初始化完成通知");
            }
            return null;
        }

        Interlocked.Increment(ref _requestCount);
        var hasParams = root.TryGetProperty("params", out var paramsProp);
        var paramsObj = hasParams ? paramsProp : default;

        _logger?.LogInformation("[McpMock] 收到请求 #{Id}: {Method}", id, method);

        try
        {
            return method switch
            {
                "initialize" => HandleInitialize(id.Value),
                "ping" => BuildResult(id.Value, new EmptyResult(), McpMockServerJsonContext.Default.EmptyResult),
                "tools/list" => HandleToolsList(id.Value),
                "tools/call" => HandleToolsCall(id.Value, paramsObj),
                "resources/list" => HandleResourcesList(id.Value),
                "resources/read" => HandleResourcesRead(id.Value),
                "prompts/list" => HandlePromptsList(id.Value),
                "prompts/get" => HandlePromptsGet(id.Value),
                "logging/setLevel" => BuildResult(id.Value, new EmptyResult(), McpMockServerJsonContext.Default.EmptyResult),
                _ => BuildErrorResponse(id.Value, -32601, $"Method not found: {method}")
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[McpMock] 处理请求 {Method} 失败", method);
            return BuildErrorResponse(id.Value, -32603, $"Internal error: {ex.Message}");
        }
    }

    /// <summary>会话 ID（首次 initialize 后生成）</summary>
    public string? SessionId => _sessionId;

    /// <summary>总请求数（统计用）</summary>
    public int RequestCount => _requestCount;

    /// <summary>工具调用次数（统计用）</summary>
    public int ToolCallCount => _toolCallCount;

    private string HandleInitialize(JsonElement id)
    {
        _sessionId ??= $"mock-{Guid.NewGuid():N}";

        var result = new InitializeResult
        {
            ProtocolVersion = _config.ProtocolVersion,
            ServerInfo = new Implementation
            {
                Name = _config.ServerName,
                Version = _config.ServerVersion
            },
            Capabilities = new ServerCapabilities
            {
                Tools = JsonDocument.Parse("{}").RootElement.Clone(),
                Resources = JsonDocument.Parse("{}").RootElement.Clone(),
                Prompts = JsonDocument.Parse("{}").RootElement.Clone()
            }
        };

        return BuildResult(id, result, McpMockServerJsonContext.Default.InitializeResult);
    }

    private string HandleToolsList(JsonElement id)
    {
        var tools = _config.Tools.Select(t => new ToolInfo
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();

        var result = new ToolsListResult { Tools = tools };
        return BuildResult(id, result, McpMockServerJsonContext.Default.ToolsListResult);
    }

    private string HandleToolsCall(JsonElement id, JsonElement paramsObj)
    {
        Interlocked.Increment(ref _toolCallCount);

        if (!paramsObj.TryGetProperty("name", out var nameProp))
        {
            return BuildErrorResponse(id, -32602, "Missing 'name' parameter");
        }

        var toolName = nameProp.GetString() ?? "";
        if (!_toolsByName.TryGetValue(toolName, out var tool))
        {
            return BuildErrorResponse(id, -32602, $"Unknown tool: {toolName}");
        }

        var hasArgs = paramsObj.TryGetProperty("arguments", out var argsProp);
        var arguments = hasArgs ? argsProp : default;

        var responseText = ExecuteTool(tool, arguments);

        var result = new CallToolResult
        {
            Content = [new McpToolContent { Type = "text", Text = responseText }],
            IsError = false
        };

        return BuildResult(id, result, McpMockServerJsonContext.Default.CallToolResult);
    }

    private string HandleResourcesList(JsonElement id)
    {
        var result = new ResourcesListResult { Resources = [] };
        return BuildResult(id, result, McpMockServerJsonContext.Default.ResourcesListResult);
    }

    private string HandleResourcesRead(JsonElement id)
    {
        var result = new ResourcesReadResult { Contents = [] };
        return BuildResult(id, result, McpMockServerJsonContext.Default.ResourcesReadResult);
    }

    private string HandlePromptsList(JsonElement id)
    {
        var result = new PromptsListResult { Prompts = [] };
        return BuildResult(id, result, McpMockServerJsonContext.Default.PromptsListResult);
    }

    private string HandlePromptsGet(JsonElement id)
    {
        var result = new PromptsGetResult
        {
            Description = "Mock prompt",
            Messages = []
        };
        return BuildResult(id, result, McpMockServerJsonContext.Default.PromptsGetResult);
    }

    /// <summary>
    /// 执行工具并返回响应文本
    /// </summary>
    private string ExecuteTool(McpToolDefinition tool, JsonElement arguments)
    {
        return tool.ResponseMode switch
        {
            "echo" => $"Echo: {tool.Name}",
            "echo_args" => EchoArguments(tool.Name, arguments),
            "fixed" => tool.FixedResponse ?? $"Fixed response from {tool.Name}",
            "echo_json" => $"{{\"tool\":\"{tool.Name}\",\"arguments\":{GetArgumentsJson(arguments)}}}",
            "add" => ExecuteAdd(arguments),
            "uppercase" => ExecuteUppercase(arguments),
            "reverse" => ExecuteReverse(arguments),
            "length" => ExecuteLength(arguments),
            _ => $"Unknown response mode: {tool.ResponseMode}"
        };
    }

    private static string EchoArguments(string toolName, JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            var parts = new List<string>();
            foreach (var prop in arguments.EnumerateObject())
            {
                parts.Add($"{prop.Name}={FormatValue(prop.Value)}");
            }
            return $"{toolName} -> {string.Join(", ", parts)}";
        }
        return $"{toolName} -> (no arguments)";
    }

    private static string ExecuteAdd(JsonElement arguments)
    {
        long a = 0, b = 0;
        if (arguments.TryGetProperty("a", out var aProp) && aProp.ValueKind == JsonValueKind.Number)
        {
            a = aProp.GetInt64();
        }
        if (arguments.TryGetProperty("b", out var bProp) && bProp.ValueKind == JsonValueKind.Number)
        {
            b = bProp.GetInt64();
        }
        return $"add({a}, {b}) = {a + b}";
    }

    private static string ExecuteUppercase(JsonElement arguments)
    {
        var text = arguments.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
        return $"uppercase: {text.ToUpperInvariant()}";
    }

    private static string ExecuteReverse(JsonElement arguments)
    {
        var text = arguments.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
        var reversed = new string(text.Reverse().ToArray());
        return $"reverse: {reversed}";
    }

    private static string ExecuteLength(JsonElement arguments)
    {
        var text = arguments.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
        return $"length: {text.Length}";
    }

    private static string GetArgumentsJson(JsonElement arguments)
    {
        return arguments.ValueKind == JsonValueKind.Undefined ? "{}" : arguments.GetRawText();
    }

    private static string FormatValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };
    }

    private static JsonElement? GetId(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idProp))
        {
            return null;
        }
        return idProp;
    }

    private static string BuildResult<T>(JsonElement id, T result, JsonTypeInfo<T> typeInfo)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Result = JsonSerializer.SerializeToElement(result, typeInfo)
        };
        return JsonSerializer.Serialize(response, McpMockServerJsonContext.Default.JsonRpcResponse);
    }

    private static string BuildErrorResponse(JsonElement? id, int code, string message)
    {
        var response = new JsonRpcResponse
        {
            Id = id ?? default,
            Error = new JsonRpcError { Code = code, Message = message }
        };
        return JsonSerializer.Serialize(response, McpMockServerJsonContext.Default.JsonRpcResponse);
    }
}
