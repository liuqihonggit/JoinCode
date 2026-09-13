
namespace JoinCode.Dream;

/// <summary>
/// 做梦模块 JSON 序列化上下文 — 为 DreamTaskDto/DreamTurnDto 生成 AOT 兼容的 JsonSerializerContext
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DreamTaskDto))]
[JsonSerializable(typeof(DreamTurnDto))]
public partial class DreamJsonContext : JsonSerializerContext;
