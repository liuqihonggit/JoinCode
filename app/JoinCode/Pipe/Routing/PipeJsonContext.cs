
namespace JoinCode.Pipe;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CodeSessionApiResponse))]
[JsonSerializable(typeof(List<CodeSessionApiResponse>))]
public partial class PipeJsonContext : JsonSerializerContext;
