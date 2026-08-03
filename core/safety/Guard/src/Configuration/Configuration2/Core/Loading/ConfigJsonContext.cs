
namespace Core.Configuration;

[JsonSourceGenerationOptions(WriteIndented = false, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(SettingsJson))]
[JsonSerializable(typeof(PermissionsSettings))]
[JsonSerializable(typeof(HookSettings))]
[JsonSerializable(typeof(McpServerSettings))]
[JsonSerializable(typeof(SandboxSettings))]
[JsonSerializable(typeof(PluginSettings))]
[JsonSerializable(typeof(WorktreeSettings))]
[JsonSerializable(typeof(StatusLineSettings))]
public partial class ConfigJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(SettingsJson))]
[JsonSerializable(typeof(PermissionsSettings))]
[JsonSerializable(typeof(HookSettings))]
[JsonSerializable(typeof(McpServerSettings))]
[JsonSerializable(typeof(SandboxSettings))]
[JsonSerializable(typeof(PluginSettings))]
[JsonSerializable(typeof(WorktreeSettings))]
[JsonSerializable(typeof(StatusLineSettings))]
public partial class ConfigIndentedJsonContext : JsonSerializerContext;
