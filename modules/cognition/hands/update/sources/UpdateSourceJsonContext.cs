namespace IO.Services.Update;

/// <summary>
/// 更新源 JSON 序列化上下文 — 注册 Release DTO + UpdateManifest，AOT 友好
/// </summary>
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GitHubReleaseDto))]
[JsonSerializable(typeof(GiteaReleaseDto))]
[JsonSerializable(typeof(GitLabReleaseDto))]
[JsonSerializable(typeof(List<GiteaReleaseDto>))]
[JsonSerializable(typeof(List<GitLabReleaseDto>))]
[JsonSerializable(typeof(UpdateManifest))]
internal sealed partial class UpdateSourceJsonContext : JsonSerializerContext;
