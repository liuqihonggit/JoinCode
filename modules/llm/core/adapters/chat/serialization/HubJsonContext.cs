
namespace Api.Chat;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HubQueryService.ChatRequest))]
[JsonSerializable(typeof(OpenAIApiMessage))]
[JsonSerializable(typeof(OpenAIChatResponse))]
[JsonSerializable(typeof(OpenAIChatChunk))]
[JsonSerializable(typeof(OpenAIChoice))]
[JsonSerializable(typeof(OpenAIToolCall))]
[JsonSerializable(typeof(OpenAIToolCallFunction))]
[JsonSerializable(typeof(ToolCallItemJson))]
internal partial class HubJsonContext : JsonSerializerContext;
