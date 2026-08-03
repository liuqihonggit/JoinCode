namespace IO.Services;

[JsonSerializable(typeof(List<PRSubscription>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
internal sealed partial class GitHubSubscriptionContext : JsonSerializerContext;
