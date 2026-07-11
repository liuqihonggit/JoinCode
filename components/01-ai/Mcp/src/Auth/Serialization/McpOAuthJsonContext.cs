
namespace McpClient;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PkceTokenResponse))]
[JsonSerializable(typeof(PkceTokenStorage))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(OAuthProtectedResourceMetadata))]
[JsonSerializable(typeof(OAuthAuthorizationServerMetadata))]
[JsonSerializable(typeof(DcrClientMetadata))]
[JsonSerializable(typeof(DcrRegistrationResult))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class McpOAuthJsonContext : JsonSerializerContext;
