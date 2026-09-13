
namespace JoinCode.Pipe;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CodeSessionApiResponse))]
[JsonSerializable(typeof(List<CodeSessionApiResponse>))]
public partial class PipeJsonContext : JsonSerializerContext;
