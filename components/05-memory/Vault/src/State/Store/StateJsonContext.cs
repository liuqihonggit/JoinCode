
namespace State;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppStateDocument))]
[JsonSerializable(typeof(SessionStateDocument))]
[JsonSerializable(typeof(ApiMessageDocument))]
[JsonSerializable(typeof(AgentStateDocument))]
[JsonSerializable(typeof(TaskStateDocument))]
[JsonSerializable(typeof(ConfigStateDocument))]
[JsonSerializable(typeof(Dictionary<string, AgentStateDocument>))]
[JsonSerializable(typeof(Dictionary<string, TaskStateDocument>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<ApiMessageDocument>))]
[JsonSerializable(typeof(List<string>))]
internal partial class StateJsonContext : JsonSerializerContext;
