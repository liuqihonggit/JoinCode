
namespace Api.Chat;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PipeQueryService.ChatRequest))]
[JsonSerializable(typeof(OpenAIApiMessage))]
[JsonSerializable(typeof(OpenAIChatResponse))]
[JsonSerializable(typeof(OpenAIChatChunk))]
[JsonSerializable(typeof(OpenAIChoice))]
[JsonSerializable(typeof(OpenAIToolCall))]
[JsonSerializable(typeof(OpenAIToolCallFunction))]
internal partial class PipeJsonContext : JsonSerializerContext;
