namespace MockServer.E2E.Tests.Core;

public enum ConversationMode
{
    Interactive,
    NonInteractive
}

public enum MockResponseType
{
    TextOnly,
    WithToolCalls,
    ToolCallOnly
}

public enum AssertType
{
    ContainsText,
    NotContainsText,
    ContainsToolCall,
    ToolCallSucceeded,
    ToolCallFailed,
    HasAssistantResponse,
    NoErrors,
    Custom
}

public sealed class ConversationScript
{
    public required string Name { get; init; }
    public required IReadOnlyList<ConversationTurn> Turns { get; init; }
    public ConversationMode Mode { get; init; } = ConversationMode.Interactive;
    public string? AdditionalArgs { get; init; }
    public Dictionary<string, string>? ExtraEnvVars { get; init; }
    public bool DumpMessages { get; init; }

    /// <summary>
    /// 额外 MockServer 脚本轮次 — 用于子进程（如 subagent）的 LLM 调用
    /// </summary>
    public IReadOnlyList<ConversationTurn>? MockServerExtraTurns { get; init; }

    /// <summary>
    /// 简单额外文本响应 — 便捷方式添加子进程用轮次
    /// 每个字符串作为一个纯文本 MockServer 轮次
    /// </summary>
    public IReadOnlyList<string>? MockServerExtraTextResponses { get; init; }

    /// <summary>
    /// 是否需要启动 Mcp.MockServer（用于测试 jcc 连接外部 MCP 服务器并调用工具的正向链路）
    /// 启动后可通过 mcp_connect 连接 http://localhost:{McpMockServerPort}/mcp
    /// </summary>
    public bool RequiresMcpMockServer { get; init; }

    /// <summary>
    /// Mcp.MockServer 使用的端口（0 = 自动分配可用端口）
    /// 仅在 RequiresMcpMockServer=true 时有效
    /// </summary>
    public int McpMockServerPort { get; init; }

    /// <summary>
    /// jcc.exe 进程的工作目录 — null 时默认使用 exe 文件所在目录
    /// 用于 AST/CodeIndex E2E 测试: 设置为有 .cs 文件的目录(如仓库根目录)使 AST 能扫描到真实代码
    /// </summary>
    public string? WorkingDirectory { get; init; }
}

public sealed class ConversationTurn
{
    public required string UserInput { get; init; }
    public required MockResponseScript AiResponse { get; init; }
    public IReadOnlyList<OutputAssert> Asserts { get; init; } = [];
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class MockResponseScript
{
    public MockResponseType Type { get; init; } = MockResponseType.TextOnly;
    public required string TextResponse { get; init; }
    public IReadOnlyList<MockToolCallScript>? ToolCalls { get; init; }
    public string? ThinkingContent { get; init; }
    public bool IsStreaming { get; init; } = true;
    public string? FollowUpText { get; init; }

    /// <summary>
    /// HTTP 状态码覆盖 — 非空时 MockServer 返回指定错误码（如 429/500/503），用于测试错误恢复
    /// </summary>
    public int? HttpStatusCode { get; init; }
}

public sealed class MockToolCallScript
{
    public required string ToolName { get; init; }
    public required string Arguments { get; init; }
    public string? ToolResult { get; init; }
}

public sealed class OutputAssert
{
    public required AssertType Type { get; init; }
    public required string Expected { get; init; }
    public string? Description { get; init; }
    public Func<string, bool>? CustomPredicate { get; init; }
}
