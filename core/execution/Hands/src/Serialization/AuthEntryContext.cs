namespace IO.Services;

[JsonSerializable(typeof(List<AuthConfigEntry>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
internal sealed partial class AuthEntryContext : JsonSerializerContext;
