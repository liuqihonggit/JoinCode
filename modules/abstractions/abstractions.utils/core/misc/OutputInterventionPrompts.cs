namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 异常输出干预 — 可疑 XML 工具调用标签检测与纠正提示词的唯一数据源。
/// <para>chat 循环与 QueryEngine 共用，避免两套判断/文案漂移。</para>
/// </summary>
public static class OutputInterventionPrompts {
    /// <summary>允许的最大连续异常输出次数，超过即强制结束。</summary>
    public const int MaxConsecutiveMalformed = 3;

    /// <summary>
    /// 检测 LLM 输出是否包含可疑的 XML 工具调用标签 — 协议层未识别但文本疑似工具调用。
    /// </summary>
    /// <param name="content">LLM 响应文本。</param>
    /// <returns>包含可疑标签返回 true。</returns>
    public static bool ContainsSuspiciousXmlTags(string content) {
        return content.Contains("<parameter ", StringComparison.Ordinal)
            || content.Contains("<antml:", StringComparison.Ordinal)
            || content.Contains("<tool_use>", StringComparison.Ordinal)
            || content.Contains("<function_calls>", StringComparison.Ordinal)
            || content.Contains("<function_call ", StringComparison.Ordinal)
            || content.Contains("<invoke ", StringComparison.Ordinal);
    }

    /// <summary>
    /// 构建异常输出纠正提示词 — 解释原因、给出正确做法，防止模型换格式绕过。
    /// </summary>
    /// <param name="count">当前连续异常次数（1 基）。</param>
    /// <param name="max">容忍上限。</param>
    /// <returns>注入用户消息的 system-reminder 文本。</returns>
    public static string BuildMalformedXmlPrompt(int count, int max = MaxConsecutiveMalformed) {
        return $"<system-reminder>系统检测到你的输出包含异常的 XML 工具调用标签" +
               $"（第{count}次，最多{max}次）。" +
               $"请使用正确的 function calling 协议输出工具调用，不要在 content 文本中输出 XML 标签。</system-reminder>";
    }
}
