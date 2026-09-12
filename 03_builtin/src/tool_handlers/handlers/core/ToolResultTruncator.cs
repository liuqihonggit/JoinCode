namespace Tools.Handlers;

/// <summary>
/// 工具结果大小限制处理 — 纯截断
/// 持久化由 ContentReplacementService 在查询引擎阶段统一处理，对齐 TS maybePersistLargeToolResult
/// </summary>
public static class ToolResultTruncator
{
    /// <summary>
    /// 截断提示信息模板 — 前缀与 ContentReplacementConstants.TruncatedPrefix 保持一致
    /// </summary>
    private const string TruncatedMessageTemplate = "\n\n" + ContentReplacementConstants.TruncatedPrefix + " output exceeded {0} characters. Use a more specific pattern or path to narrow results.]";

    /// <summary>
    /// 从 StringBuilder 构建带大小限制的结果
    /// 对齐 TS maxResultSizeChars — 仅截断，持久化由 ContentReplacementService 统一处理
    /// </summary>
    public static ToolResult BuildWithSizeLimit(StringBuilder response, int maxResultSizeChars)
    {
        var text = response.ToString();
        if (text.Length <= maxResultSizeChars)
        {
            return ToolResultBuilder.Success().WithText(text).Build();
        }

        var truncatedText = TruncateAtNewline(text, maxResultSizeChars);
        return ToolResultBuilder.Success().WithText(truncatedText).Build();
    }

    /// <summary>
    /// 对齐 TS generatePreview: 在换行符处截断，避免切断行内容
    /// </summary>
    public static string TruncateAtNewline(string text, int maxResultSizeChars)
    {
        if (text.Length <= maxResultSizeChars)
            return text;

        var lastNewline = text.AsSpan(0, maxResultSizeChars).LastIndexOf('\n');
        var cutPoint = lastNewline > maxResultSizeChars / 2 ? lastNewline : maxResultSizeChars;

        var sb = new StringBuilder(cutPoint + 128);
        sb.Append(text, 0, cutPoint);
        sb.AppendFormat(TruncatedMessageTemplate, maxResultSizeChars);
        return sb.ToString();
    }
}
