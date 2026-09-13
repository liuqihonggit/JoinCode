namespace JoinCode.ChatCommands;

/// <summary>
/// 斜杠命令 → MCP 工具适配器 — 把 IChatCommand 包装为 IToolHandler，让 LLM 能通过 MCP 协议调用斜杠命令
/// </summary>
public sealed class SlashToMcpAdapter : IToolHandler

{
    private readonly IChatCommand _command;
    private readonly IServiceProvider _serviceProvider;
    private readonly ToolKind _kind;

    /// <summary>
    /// 构造 — command 为斜杠命令，serviceProvider 用于构造 ChatCommandContext
    /// </summary>
    public SlashToMcpAdapter(IChatCommand command, IServiceProvider serviceProvider, ToolKind kind = ToolKind.Slash)
    {
        _command = command;
        _serviceProvider = serviceProvider;
        _kind = kind;
    }

    /// <summary>工具名称 — 透传斜杠命令名</summary>
    public string Name => _command.Name;
    /// <summary>工具描述 — 透传斜杠命令描述</summary>
    public string Description => _command.Description;
    /// <summary>工具种类 — 构造时指定</summary>
    public ToolKind Kind => _kind;
    /// <summary>工具分组名 — 固定为 "slash"</summary>
    public string? GroupName => "slash";
    /// <summary>工具分类 — 固定为 "skill"</summary>
    public string? Category => "skill";
    /// <summary>超时策略 — 固定为 None</summary>
    public ToolTimeoutPolicy TimeoutPolicy => ToolTimeoutPolicy.None;

    /// <summary>
    /// 自动生成 schema — {"arguments": string}
    /// </summary>
    public ToolSchema InputSchema { get; } = BuildSchema();

    /// <summary>
    /// 执行 — 从 JSON 参数提取 "arguments" 字段，构造 ChatCommandContext，调用斜杠命令
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        // 从 JSON 参数提取 arguments 字符串
        var args = string.Empty;
        if (arguments.TryGetValue("arguments", out var argElement) && argElement.ValueKind == JsonValueKind.String)
        {
            args = argElement.GetString() ?? string.Empty;
        }

        var context = new ChatCommandContext
        {
            Arguments = args,
            CancellationToken = cancellationToken,
            Services = _serviceProvider,
        };

        var result = await _command.ExecuteAsync(context).ConfigureAwait(false);

        // ChatCommandResult → ToolResult
        // 斜杠命令通过 TerminalHelper 直接输出，不返回结构化内容
        // ToolResult 只需要标记是否成功
        return new ToolResult
        {
            Content = [new ToolContent { Type = ToolContentType.Text, Text = result.IsHandled ? "命令已执行" : "命令未处理" }],
            IsError = false,
        };
    }

    private static ToolSchema BuildSchema()
    {
        var schema = new ToolSchema();
        schema.Properties["arguments"] = new ToolSchemaProperty
        {
            Type = "string",
            Description = "命令参数",
        };
        return schema;
    }
}
