
namespace Api.LLM;

/// <summary>
/// Jev API 专用 JsonContext — 源码生成器注册所有 Jev DTO,保证 NativeAOT 兼容
/// JSON 读写通过 RelaxedJsonSerializer.Serialize/Deserialize(value, JevJsonContext.Default.XxxType)
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JevRequest))]
[JsonSerializable(typeof(JevResponse))]
[JsonSerializable(typeof(JevQuestion))]
[JsonSerializable(typeof(JevAnswer))]
[JsonSerializable(typeof(JevUsage))]
[JsonSerializable(typeof(Dictionary<string, JevQuestion>))]
[JsonSerializable(typeof(Dictionary<string, JevAnswer>))]
internal partial class JevJsonContext : JsonSerializerContext;
