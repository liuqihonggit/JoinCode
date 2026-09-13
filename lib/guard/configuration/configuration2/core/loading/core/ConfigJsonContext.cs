
namespace Core.Configuration;

/// <summary>
/// 配置 JSON 序列化上下文 — 紧凑格式(camelCase + 无缩进 + 跳过注释 + 允许尾逗号),供配置读写高性能序列化使用
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(SettingsJson))]
[JsonSerializable(typeof(CurrentSettings))]
[JsonSerializable(typeof(PermissionsSettings))]
[JsonSerializable(typeof(HookSettings))]
[JsonSerializable(typeof(McpServerSettings))]
[JsonSerializable(typeof(SandboxSettings))]
[JsonSerializable(typeof(PluginSettings))]
[JsonSerializable(typeof(WorktreeSettings))]
[JsonSerializable(typeof(StatusLineSettings))]
[JsonSerializable(typeof(ProfileSettings))]
[JsonSerializable(typeof(ModelItemConfig))]
[JsonSerializable(typeof(ModelCapabilitiesConfig))]
[JsonSerializable(typeof(ModelModalityKind))]
[JsonSerializable(typeof(ModelPricingConfig))]
[JsonSerializable(typeof(UpdateSourceConfig))]
public partial class ConfigJsonContext : JsonSerializerContext;

/// <summary>
/// 配置 JSON 序列化上下文 — 缩进格式(camelCase + 缩进 + 跳过注释 + 允许尾逗号),供配置文件可读性持久化使用
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(SettingsJson))]
[JsonSerializable(typeof(CurrentSettings))]
[JsonSerializable(typeof(PermissionsSettings))]
[JsonSerializable(typeof(HookSettings))]
[JsonSerializable(typeof(McpServerSettings))]
[JsonSerializable(typeof(SandboxSettings))]
[JsonSerializable(typeof(PluginSettings))]
[JsonSerializable(typeof(WorktreeSettings))]
[JsonSerializable(typeof(StatusLineSettings))]
[JsonSerializable(typeof(ProfileSettings))]
[JsonSerializable(typeof(ModelItemConfig))]
[JsonSerializable(typeof(ModelCapabilitiesConfig))]
[JsonSerializable(typeof(ModelModalityKind))]
[JsonSerializable(typeof(ModelPricingConfig))]
[JsonSerializable(typeof(UpdateSourceConfig))]
public partial class ConfigIndentedJsonContext : JsonSerializerContext;
