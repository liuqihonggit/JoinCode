namespace JoinCode.Cli;

// ─── CompactSummaryData / CompactSummaryRenderer ───

/// <summary>
/// 压缩摘要数据
/// </summary>
public sealed class CompactSummaryData
{
    /// <summary>
    /// 压缩前消息数
    /// </summary>
    public int MessagesBefore { get; init; }

    /// <summary>
    /// 压缩后消息数
    /// </summary>
    public int MessagesAfter { get; init; }

    /// <summary>
    /// 节省的 Token 数
    /// </summary>
    public int TokensSaved { get; init; }

    /// <summary>
    /// 已摘要的消息数
    /// </summary>
    public int MessagesSummarized { get; init; }

    /// <summary>
    /// 压缩方向
    /// </summary>
    public CompactDirection Direction { get; init; }

    /// <summary>
    /// 原始 Token 数
    /// </summary>
    public int OriginalTokens { get; init; }

    /// <summary>
    /// 压缩后 Token 数
    /// </summary>
    public int CompressedTokens { get; init; }
}

/// <summary>
/// 压缩摘要渲染器 — CLI 简化版
/// </summary>
public sealed class CompactSummaryRenderer
{
    /// <summary>
    /// 渲染压缩摘要数据为多行文本
    /// </summary>
    /// <param name="data">压缩摘要数据</param>
    /// <returns>渲染后的文本</returns>
    public string Render(CompactSummaryData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{TerminalColors.Primary}上下文已压缩{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine($"  消息: {data.MessagesSummarized} 条已摘要");
        sb.AppendLine($"  Token: {data.OriginalTokens:N0} → {data.CompressedTokens:N0} (节省 {data.OriginalTokens - data.CompressedTokens:N0})");
        return sb.ToString();
    }

    /// <summary>
    /// 静态渲染压缩摘要为单行简短文本
    /// </summary>
    /// <param name="data">压缩摘要数据</param>
    /// <returns>渲染后的单行文本</returns>
    public static string RenderStatic(CompactSummaryData data)
    {
        return $"上下文已压缩: {data.MessagesBefore} → {data.MessagesAfter} 消息, 节省 {data.TokensSaved:N0} tokens";
    }
}

