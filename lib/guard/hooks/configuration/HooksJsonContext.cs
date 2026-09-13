namespace Core.Hooks.Configuration;

/// <summary>
/// 钩子 JSON 序列化上下文 — 为钩子配置、输入、决策等类型提供 AOT 友好的源码生成序列化器
/// </summary>
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
