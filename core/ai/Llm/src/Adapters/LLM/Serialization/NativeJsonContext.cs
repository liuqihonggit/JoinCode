
namespace Api.LLM;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OpenAIChatRequest))]
[JsonSerializable(typeof(OpenAIApiMessage))]
[JsonSerializable(typeof(OpenAIChatResponse))]
[JsonSerializable(typeof(OpenAIChoice))]
[JsonSerializable(typeof(OpenAIChatChunk))]
[JsonSerializable(typeof(OpenAIUsage))]
[JsonSerializable(typeof(OpenAIPromptTokensDetails))]
[JsonSerializable(typeof(OpenAIStreamOptions))]
[JsonSerializable(typeof(OpenAITool))]
[JsonSerializable(typeof(OpenAIFunctionDefinition))]
[JsonSerializable(typeof(OpenAIFunctionParameters))]
[JsonSerializable(typeof(OpenAIParameterProperty))]
[JsonSerializable(typeof(OpenAIToolCall))]
[JsonSerializable(typeof(OpenAIToolCallFunction))]
[JsonSerializable(typeof(TokenUsage))]
[JsonSerializable(typeof(List<OpenAIToolCall>))]
internal partial class NativeJsonContext : JsonSerializerContext;
