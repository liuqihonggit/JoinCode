
namespace Core.Summary;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AwaySummaryResult))]
[JsonSerializable(typeof(AwayEvent))]
[JsonSerializable(typeof(List<AwayEvent>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
internal sealed partial class AwaySummaryJsonContext : JsonSerializerContext;
