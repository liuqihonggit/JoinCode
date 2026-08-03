namespace MockServer.Core;

[JsonSerializable(typeof(CapturedRequest))]
[JsonSerializable(typeof(MockServerStats))]
[JsonSerializable(typeof(CacheStats))]
[JsonSerializable(typeof(MockServerConfig))]
[JsonSerializable(typeof(ScriptedTurn))]
[JsonSerializable(typeof(ToolCallConfig))]
[JsonSerializable(typeof(List<ScriptedTurn>))]
[JsonSerializable(typeof(List<ToolCallConfig>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
public sealed partial class MockServerJsonContext : JsonSerializerContext;
