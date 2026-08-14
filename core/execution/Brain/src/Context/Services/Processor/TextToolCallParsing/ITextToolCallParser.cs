namespace Core.Context;

/// <summary>
/// 文本工具调用解析器接口 — 从 LLM content 文本中解析工具调用
/// 用于协议字段(tool_calls/tool_use)未走通时的 fallback：
/// 某些模型不支持 function calling 协议，会在 content 里输出文本格式的工具调用
/// </summary>
public interface ITextToolCallParser
{
    /// <summary>
    /// 尝试从 LLM 完整响应文本中解析工具调用
    /// </summary>
    /// <param name="content">LLM 完整响应文本（已累积完毕）</param>
    /// <returns>解析成功返回结果（含工具调用列表），无法识别返回 null</returns>
    TextToolCallParseResult? TryParse(ReadOnlySpan<char> content);
}
