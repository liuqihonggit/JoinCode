
namespace JoinCode.Abstractions.Configuration.Llm;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ModelConfigRoot))]
[JsonSerializable(typeof(ModelProviderConfig))]
[JsonSerializable(typeof(ModelItemConfig))]
[JsonSerializable(typeof(ModelCapabilitiesConfig))]
[JsonSerializable(typeof(ModelPricingConfig))]
public partial class ModelConfigJsonContext : JsonSerializerContext;
