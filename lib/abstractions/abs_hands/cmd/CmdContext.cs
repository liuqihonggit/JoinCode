namespace JoinCode.Abstractions.Cmd;

/// <summary>
/// 统一命令上下文 — 斜杠命令和 MCP 工具的调用参数统一容器
/// </summary>
public sealed class CmdContext
{
    /// <summary>取消令牌</summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>触发来源 — 谁调用的命令</summary>
    public CmdSource TriggerSource { get; init; }

    // === 斜杠命令参数 ===

    /// <summary>斜杠命令参数文本（如 "/commit msg" 的 "msg"）</summary>
    public string Arguments { get; init; } = "";

    /// <summary>DI 服务容器 — 斜杠命令执行时需要</summary>
    public IServiceProvider? Services { get; init; }

    /// <summary>会话 ID</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>会话开始时间</summary>
    public DateTime SessionStartedAt { get; init; } = DateTime.UtcNow;

    // === MCP 工具参数 ===

    /// <summary>MCP 工具的 JSON 参数</summary>
    public Dictionary<string, JsonElement> JsonArgs { get; init; } = [];

    /// <summary>MCP 工具进度回调</summary>
    public ToolProgressCallback? OnProgress { get; init; }

    // === 工厂方法 ===

    /// <summary>为斜杠命令创建上下文</summary>
    public static CmdContext ForSlash(string arguments, CancellationToken ct, IServiceProvider services) => new()
    {
        CancellationToken = ct,
        TriggerSource = CmdSource.Slash,
        Arguments = arguments,
        Services = services,
    };

    /// <summary>为 MCP 工具创建上下文</summary>
    public static CmdContext ForMcp(
        Dictionary<string, JsonElement> jsonArgs,
        CancellationToken ct,
        ToolProgressCallback? onProgress = null) => new()
    {
        CancellationToken = ct,
        TriggerSource = CmdSource.Mcp,
        JsonArgs = jsonArgs,
        OnProgress = onProgress,
    };

    // === 转换到原上下文 ===

    /// <summary>转换为 ChatCommandContext（斜杠命令执行用）</summary>
    public ChatCommandContext ToSlashContext()
    {
        // 如果 Arguments 为空但 JsonArgs 有 "arguments" 字段，从中提取（LLM 调斜杠命令时参数是 JSON）
        var arguments = Arguments;
        if (string.IsNullOrEmpty(arguments)
            && JsonArgs is not null
            && JsonArgs.TryGetValue("arguments", out var argElement)
            && argElement.ValueKind == JsonValueKind.String)
        {
            arguments = argElement.GetString() ?? "";
        }

        return new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken,
            Services = Services ?? throw new InvalidOperationException("CmdContext.Services 未设置，无法执行斜杠命令"),
            SessionId = SessionId,
            SessionStartedAt = SessionStartedAt,
        };
    }

    /// <summary>获取 MCP 参数</summary>
    public Dictionary<string, JsonElement> ToMcpArgs() => JsonArgs ?? [];
}
