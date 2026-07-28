namespace IO.Services;

[JsonSerializable(typeof(List<AuthConfigEntry>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class AuthEntryContext : JsonSerializerContext;
