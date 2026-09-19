
namespace JoinCode.Pipe;

/// <summary>
/// 管道 JSON 序列化上下文 — 为 CodeSessionApiResponse 及其列表生成 AOT 兼容的源码序列化器
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CodeSessionApiResponse))]
[JsonSerializable(typeof(List<CodeSessionApiResponse>))]
public partial class PipeJsonContext : JsonSerializerContext;