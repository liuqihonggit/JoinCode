namespace JoinCode.Abstractions.LLM.Execution;

/// <summary>
/// 文本工具调用 — LLM 未走 function calling 协议时，从 content 文本解析出的调用。
/// </summary>
/// <param name="Name">工具名称。</param>
/// <param name="Arguments">参数 JSON 文本。</param>
/// <param name="Id">调用标识；文本格式通常无 ID，可为 null。</param>
public sealed record TextToolCallFallbackCall(string Name, string Arguments, string? Id);

/// <summary>
/// 文本工具调用解析结果 — 供 QueryEngine 与 chat 循环共用。
/// </summary>
public sealed record TextToolCallFallbackResult {
    /// <summary>解析出的工具调用（至少 1 个）。</summary>
    public required IReadOnlyList<TextToolCallFallbackCall> ToolCalls { get; init; }

    /// <summary>工具调用块之外的剩余纯文本；无剩余时为 null。</summary>
    public string? RemainingText { get; init; }
}

/// <summary>
/// 文本工具调用 fallback 解析器 — 从 LLM content 解析文本格式工具调用。
/// <para>
/// 实现位于 chat 服务域（DSML/antml 等格式责任链），QueryEngine 经 DI 可选解析，
/// 禁止 query 域反向引用 chat 域。
/// </para>
/// </summary>
public interface ITextToolCallFallbackParser {
    /// <summary>
    /// 尝试从完整响应文本中解析工具调用。
    /// </summary>
    /// <param name="content">LLM 完整响应文本。</param>
    /// <returns>解析成功返回结果；无法识别返回 null。</returns>
    TextToolCallFallbackResult? TryParse(ReadOnlySpan<char> content);
}
