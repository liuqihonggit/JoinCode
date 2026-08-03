
namespace JoinCode.Dream;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DreamTaskDto))]
[JsonSerializable(typeof(DreamTurnDto))]
public partial class DreamJsonContext : JsonSerializerContext;
