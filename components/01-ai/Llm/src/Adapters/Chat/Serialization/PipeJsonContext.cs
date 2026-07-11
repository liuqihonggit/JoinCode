
namespace Api.Chat;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PipeQueryService.ChatRequest))]
[JsonSerializable(typeof(PipeQueryService.Message))]
[JsonSerializable(typeof(PipeQueryService.ToolCall))]
[JsonSerializable(typeof(PipeQueryService.ToolCallFunction))]
[JsonSerializable(typeof(PipeQueryService.ChatCompletionResponse))]
[JsonSerializable(typeof(PipeQueryService.Choice))]
[JsonSerializable(typeof(PipeQueryService.ChatCompletionChunk))]
internal partial class PipeJsonContext : JsonSerializerContext;
