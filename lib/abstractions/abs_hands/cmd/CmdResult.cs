namespace JoinCode.Abstractions.Cmd;

/// <summary>
/// 统一命令结果 — 斜杠命令和 MCP 工具的执行结果统一包装
/// </summary>
public sealed record CmdResult
{
    /// <summary>是否继续聊天循环（斜杠命令语义，MCP 忽略）</summary>
    public bool ShouldContinue { get; init; } = true;

    /// <summary>是否已处理（斜杠命令语义）</summary>
    public bool IsHandled { get; init; } = true;

    /// <summary>内容列表 — MCP 工具返回的结构化内容</summary>
    public List<ToolContent> Content { get; init; } = [];

    /// <summary>是否错误</summary>
    public bool IsError { get; init; }

    /// <summary>从斜杠命令结果转换</summary>
    public static CmdResult FromSlashResult(ChatCommandResult r) => new()
    {
        ShouldContinue = r.ShouldContinue,
        IsHandled = r.IsHandled,
    };

    /// <summary>从 MCP 工具结果转换</summary>
    public static CmdResult FromMcpResult(ToolResult r) => new()
    {
        Content = r.Content,
        IsError = r.IsError,
    };
}
