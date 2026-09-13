namespace Core.Permission;

/// <summary>
/// 权限 JSON 序列化上下文 — 为代理权限规则列表生成 AOT 兼容的 JSON 序列化代码
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<AgentPermissionRule>))]
public partial class PermissionJsonContext : JsonSerializerContext;
