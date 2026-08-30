
namespace Core.Configuration;

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
public partial class ConfigJsonContext : JsonSerializerContext;

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
public partial class ConfigIndentedJsonContext : JsonSerializerContext;

/// <summary>
/// 配置 JSON 序列化选项 — 在源码生成 options 基础上注入 UnsafeRelaxedJsonEscaping Encoder，
/// 使中文字符以真实 UTF-8 输出而非 \uXXXX 转义。源码生成器 attribute 无法引用静态 Encoder 属性，
/// 故在此运行时创建副本。CamelCase 命名策略已由 JsonSourceGenerationOptions 声明，副本继承。
/// </summary>
public static class ConfigJsonOptions
{
    /// <summary>紧凑（无缩进）+ 真实中文 + camelCase。</summary>
    public static readonly JsonSerializerOptions Compact =
        new(ConfigJsonContext.Default.Options) { TypeInfoResolver = ConfigJsonContext.Default, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>缩进 + 真实中文 + camelCase。</summary>
    public static readonly JsonSerializerOptions Indented =
        new(ConfigIndentedJsonContext.Default.Options) { TypeInfoResolver = ConfigIndentedJsonContext.Default, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>序列化为缩进 JSON（真实中文 + camelCase）。TypeInfoResolver 为源码生成，AOT 安全。</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TypeInfoResolver 为源码生成的 ConfigIndentedJsonContext.Default，所有类型已静态 rooted，AOT 安全。")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "TypeInfoResolver 为源码生成的 ConfigIndentedJsonContext.Default，无需运行时反射 emit。")]
    public static string SerializeIndented<T>(T value) => JsonSerializer.Serialize(value, Indented);

    /// <summary>序列化为紧凑 JSON（真实中文 + camelCase）。TypeInfoResolver 为源码生成，AOT 安全。</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TypeInfoResolver 为源码生成的 ConfigJsonContext.Default，所有类型已静态 rooted，AOT 安全。")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "TypeInfoResolver 为源码生成的 ConfigJsonContext.Default，无需运行时反射 emit。")]
    public static string SerializeCompact<T>(T value) => JsonSerializer.Serialize(value, Compact);
}
