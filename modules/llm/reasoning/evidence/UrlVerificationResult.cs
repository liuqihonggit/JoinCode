namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 证据链接验证结果
/// </summary>
public sealed class UrlVerificationResult {
    /// <summary>
    /// 待验证的 URL
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// URL 格式是否合法
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// URL 是否可访问
    /// </summary>
    public bool IsAccessible { get; init; }

    /// <summary>
    /// 页面内容是否包含期望文本
    /// </summary>
    public bool ContainsExpectedText { get; init; }

    /// <summary>
    /// 期望文本在页面中出现的行号；未找到时为 null
    /// </summary>
    public int? FoundAtLine { get; init; }

    /// <summary>
    /// 从页面抽取的文本片段；未抽取时为 null
    /// </summary>
    public string? ExtractedText { get; init; }

    /// <summary>
    /// 验证过程中的错误信息；无错误时为 null
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// 验证是否因超时而失败
    /// </summary>
    public bool IsTimeout { get; init; }

    /// <summary>
    /// 验证发生的时间；未执行验证时为 null
    /// </summary>
    public DateTime? VerificationTime { get; init; }
}