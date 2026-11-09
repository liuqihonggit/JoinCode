namespace Tools.Handlers;

/// <summary>
/// 工具创建 JSON 序列化上下文 — 为工具参数 DTO 列表生成 AOT 兼容的 JSON 序列化代码
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<ToolParameterDto>))]
public partial class StructuredOutputJsonContext : JsonSerializerContext;
