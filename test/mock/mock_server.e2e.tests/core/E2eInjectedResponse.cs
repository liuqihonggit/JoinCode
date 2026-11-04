namespace MockServer.E2E.Tests.Core;

[JsonSerializable(typeof(List<E2eInjectedResponse>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
public sealed partial class E2eJsonContext : JsonSerializerContext;

public sealed class E2eInjectedResponse {
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "application/json";
    public string Body { get; set; } = "{}";
    public bool IsStream { get; set; }
    public List<string> StreamChunks { get; set; } = [];
    public int DelayMs { get; set; }
}
