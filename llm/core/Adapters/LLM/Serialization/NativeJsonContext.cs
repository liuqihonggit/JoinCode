
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
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
[JsonSerializable(typeof(OpenAIThinkingOptions))]
[JsonSerializable(typeof(OpenAIMessageContent))]
[JsonSerializable(typeof(OpenAIContentPart))]
[JsonSerializable(typeof(OpenAIImageUrl))]
[JsonSerializable(typeof(List<OpenAIContentPart>))]
[JsonSerializable(typeof(ResponsesRequest))]
[JsonSerializable(typeof(ResponsesResponse))]
[JsonSerializable(typeof(ResponsesOutputItem))]
[JsonSerializable(typeof(ResponsesContent))]
[JsonSerializable(typeof(ResponsesUsage))]
[JsonSerializable(typeof(ResponsesTokenDetails))]
[JsonSerializable(typeof(ResponsesReasoning))]
[JsonSerializable(typeof(ResponsesTool))]
[JsonSerializable(typeof(TokenUsage))]
[JsonSerializable(typeof(List<OpenAIToolCall>))]
[JsonSerializable(typeof(ToolCallItemJson))]
[JsonSerializable(typeof(ResponsesInputItemDto))]
[JsonSerializable(typeof(ResponsesInputContentDto))]
[JsonSerializable(typeof(List<ResponsesInputItemDto>))]
[JsonSerializable(typeof(JsonSchemaDto))]
[JsonSerializable(typeof(JsonSchemaPropertyDto))]
[JsonSerializable(typeof(ResponsesDeltaEvent))]
[JsonSerializable(typeof(ResponsesFunctionCallArgsDeltaEvent))]
[JsonSerializable(typeof(ResponsesFunctionCallItem))]
[JsonSerializable(typeof(ResponsesEventEnvelope))]
[JsonSerializable(typeof(ResponsesEventResponse))]
[JsonSerializable(typeof(ToolCallMetadataDto))]
[JsonSerializable(typeof(AnthropicWebSearchLinkDto))]
[JsonSerializable(typeof(List<AnthropicWebSearchLinkDto>))]
[JsonSerializable(typeof(AnthropicWebSearchErrorDto))]
internal partial class NativeJsonContext : JsonSerializerContext {

    private static readonly Lazy<NativeJsonContext> s_safe = new(() => new NativeJsonContext(
        new JsonSerializerOptions(Default!.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = Default!
        }));

    /// <summary>
    /// 带 UnsafeRelaxedJsonEscaping 的上下文 — 不转义中文等非 ASCII 字符
    /// </summary>
    public static NativeJsonContext Safe => s_safe.Value;
}