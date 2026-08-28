namespace JoinCode.Vision.Serialization;

/// <summary>
/// Vision 子系统 AOT 兼容 JSON 序列化上下文 — 四叉树染色映射 + M2 隐喻拓扑模型
/// </summary>
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(ImageDescriptionResult))]
[JsonSerializable(typeof(ImageDrillDownResult))]
[JsonSerializable(typeof(ImageLabel))]
[JsonSerializable(typeof(ImageAttribute))]
[JsonSerializable(typeof(List<ImageLabel>))]
[JsonSerializable(typeof(List<ImageAttribute>))]
[JsonSerializable(typeof(List<string>))]
public sealed partial class VisionJsonContext : JsonSerializerContext;
