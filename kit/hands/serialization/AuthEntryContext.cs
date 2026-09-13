namespace IO.Services;

[JsonSerializable(typeof(List<AuthConfigEntry>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
internal sealed partial class AuthEntryContext : JsonSerializerContext;
