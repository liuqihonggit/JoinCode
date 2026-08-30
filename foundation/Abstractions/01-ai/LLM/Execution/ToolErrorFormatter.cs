namespace JoinCode.Abstractions.LLM.Execution;

/// <summary>
/// 工具错误消息格式化器 — 解析 &lt;tool_use_error&gt;...&lt;/tool_use_error&gt; 标签为友好消息。
/// 三套 UI（CLI/TUI/GUI）共用，避免把原始标签暴露给用户。
/// 回归背景：SchemaValidationMiddleware / ChatToolOrchestrator 把错误包在 &lt;tool_use_error&gt; 标签里，
/// 直接显示让用户看到 XML 标签，且 200 字符截断常切断关键错误信息。
/// </summary>
public static class ToolErrorFormatter
{
    private const string StartTag = "<tool_use_error>";
    private const string EndTag = "</tool_use_error>";

    /// <summary>
    /// 从结果文本中提取错误消息。若含 &lt;tool_use_error&gt; 标签则返回标签内内容（去标签）；
    /// 否则返回原文。仅当 isError=true 时解析标签，正常结果原样返回。
    /// </summary>
    /// <param name="resultText">工具结果原始文本。</param>
    /// <param name="isError">是否为错误结果。</param>
    /// <returns>友好的错误消息，或原文。</returns>
    public static string ExtractMessage(string? resultText, bool isError)
    {
        if (string.IsNullOrEmpty(resultText))
            return string.Empty;

        if (!isError)
            return resultText!;

        var startIdx = resultText!.IndexOf(StartTag, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0)
            return resultText!;

        var contentStart = startIdx + StartTag.Length;
        var endIdx = resultText.IndexOf(EndTag, contentStart, StringComparison.OrdinalIgnoreCase);
        if (endIdx <= contentStart)
            return resultText![contentStart..].Trim();

        return resultText[contentStart..endIdx].Trim();
    }
}
