namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 证据链接验证结果
/// </summary>
public sealed class UrlVerificationResult
{
    public required string Url { get; init; }
    public bool IsValid { get; init; }
    public bool IsAccessible { get; init; }
    public bool ContainsExpectedText { get; init; }
    public int? FoundAtLine { get; init; }
    public string? ExtractedText { get; init; }
    public string? Error { get; init; }
    public bool IsTimeout { get; init; }
    public DateTime? VerificationTime { get; init; }
}
