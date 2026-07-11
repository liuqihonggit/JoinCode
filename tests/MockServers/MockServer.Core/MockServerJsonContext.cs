namespace MockServer.Core;

[JsonSerializable(typeof(CapturedRequest))]
[JsonSerializable(typeof(MockServerStats))]
[JsonSerializable(typeof(CacheStats))]
[JsonSerializable(typeof(MockServerConfig))]
[JsonSerializable(typeof(ScriptedTurn))]
[JsonSerializable(typeof(ToolCallConfig))]
[JsonSerializable(typeof(List<ScriptedTurn>))]
[JsonSerializable(typeof(List<ToolCallConfig>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public sealed partial class MockServerJsonContext : JsonSerializerContext;
