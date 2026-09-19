namespace MockServer.E2E.Tests.Core;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(ChatCompletionChunk))]
[JsonSerializable(typeof(MockErrorResponse))]
internal sealed partial class MockServerE2EJsonContext : JsonSerializerContext;

public sealed record MockErrorResponse(MockErrorDetail error);
public sealed record MockErrorDetail(string message, string type, string? code);