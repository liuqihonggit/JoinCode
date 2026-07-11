
namespace Testing.Common.MockServer;

[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ApiMessage))]
[JsonSerializable(typeof(MockChatCompletionResponse))]
[JsonSerializable(typeof(MockChatChoice))]
[JsonSerializable(typeof(MockApiMessage))]
[JsonSerializable(typeof(MockToolCall))]
[JsonSerializable(typeof(MockToolCallFunction))]
[JsonSerializable(typeof(MockChatDelta))]
[JsonSerializable(typeof(MockToolCallDelta))]
[JsonSerializable(typeof(MockToolCallFunctionDelta))]
[JsonSerializable(typeof(MockUsage))]
[JsonSerializable(typeof(MockModelsResponse))]
[JsonSerializable(typeof(MockModelItem))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class MockServerJsonContext : JsonSerializerContext;
