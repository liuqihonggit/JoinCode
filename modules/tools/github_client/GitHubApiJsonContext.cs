namespace IO.ProcessService;

/// <summary>
/// GitHub API JSON 序列化上下文 — AOT 兼容
/// 仅注册 GitHubApiClient 所需的类型
/// </summary>
[JsonSerializable(typeof(GitHubErrorMessageDto))]
[JsonSerializable(typeof(GitHubErrorDetailDto))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class GitHubApiJsonContext : JsonSerializerContext;

/// <summary>GitHub API 错误响应体 — 提取 message + errors 数组</summary>
public sealed class GitHubErrorMessageDto {
    /// <summary>错误消息</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>详细错误列表（422 Validation Failed 时包含具体原因）</summary>
    [JsonPropertyName("errors")]
    public List<GitHubErrorDetailDto> Errors { get; set; } = [];
}

/// <summary>GitHub API 错误详情项 — errors 数组元素</summary>
public sealed class GitHubErrorDetailDto {
    /// <summary>错误详情消息</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>错误关联字段</summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>错误关联资源</summary>
    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    /// <summary>错误代码</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
