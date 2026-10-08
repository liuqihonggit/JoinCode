namespace IO.ProcessService;

/// <summary>
/// GitHub API JSON 序列化上下文 — AOT 兼容
/// 仅注册 GitHubApiClient 所需的类型
/// </summary>
[JsonSerializable(typeof(GitHubErrorMessageDto))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class GitHubApiJsonContext : JsonSerializerContext;

/// <summary>GitHub API 错误响应体 — 提取 message</summary>
public sealed class GitHubErrorMessageDto {
    /// <summary>错误消息</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
