namespace Core.Agents.Coordinator;

/// <summary>
/// Teammate 初始化 JSON 源码生成上下文 — 为 TeammateIdleNotification 等类型生成 AOT 兼容的序列化器
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TeammateIdleNotification))]
internal sealed partial class TeammateInitJsonContext : JsonSerializerContext;
