
namespace JoinCode.Abstractions.Configuration.Llm;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ModelConfigRoot))]
[JsonSerializable(typeof(ModelProviderConfig))]
[JsonSerializable(typeof(ModelItemConfig))]
[JsonSerializable(typeof(ModelCapabilitiesConfig))]
public partial class ModelConfigJsonContext : JsonSerializerContext;
