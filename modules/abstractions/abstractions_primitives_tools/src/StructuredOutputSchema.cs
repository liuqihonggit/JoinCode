namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 验证错误详情
/// </summary>
public sealed class ValidationError {
    /// <summary>获取错误路径。</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>获取错误消息。</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
