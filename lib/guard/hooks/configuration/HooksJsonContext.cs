namespace Core.Hooks.Configuration;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HookSettingsFile))]
[JsonSerializable(typeof(Dictionary<string, List<HookMatcher>>))]
[JsonSerializable(typeof(HookInput))]
[JsonSerializable(typeof(PermissionUpdate))]
[JsonSerializable(typeof(List<PermissionUpdate>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(HookHttpPayload))]
[JsonSerializable(typeof(HookDecision))]
[JsonSerializable(typeof(string))]
public partial class HooksJsonContext : JsonSerializerContext;
