namespace Tools.Handlers;

/// <summary>
/// 结构化输出 JSON 序列化上下文 — 为结构化输出模式列表生成 AOT 兼容的 JSON 序列化代码
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<StructuredOutputSchema>))]
public partial class StructuredOutputJsonContext : JsonSerializerContext;