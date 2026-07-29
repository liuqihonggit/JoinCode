
namespace Services.Api.Vcr;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VcrCassette))]
[JsonSerializable(typeof(VcrInteraction))]
[JsonSerializable(typeof(VcrRequest))]
[JsonSerializable(typeof(VcrResponse))]
[JsonSerializable(typeof(List<VcrInteraction>))]
[JsonSerializable(typeof(string))]
internal sealed partial class VcrJsonContext : JsonSerializerContext;
