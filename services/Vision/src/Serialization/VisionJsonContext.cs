namespace JoinCode.Vision.Serialization;

/// <summary>
/// Vision 子系统 AOT 兼容 JSON 序列化上下文 — 四叉树染色映射等结构化数据
/// </summary>
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Dictionary<string, double>))]
public sealed partial class VisionJsonContext : JsonSerializerContext;
