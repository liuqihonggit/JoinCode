namespace IO.Services;

[JsonSerializable(typeof(List<PRSubscription>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class GitHubSubscriptionContext : JsonSerializerContext;
