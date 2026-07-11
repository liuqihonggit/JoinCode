
namespace JoinCode.Dream;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DreamTaskDto))]
[JsonSerializable(typeof(DreamTurnDto))]
public partial class DreamJsonContext : JsonSerializerContext;
