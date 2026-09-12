
namespace Services.Api.Vcr;

public sealed class VcrCassette
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("interactions")]
    public List<VcrInteraction> Interactions { get; set; } = new();
}

public sealed class VcrInteraction
{
    [JsonPropertyName("request")]
    public VcrRequest Request { get; set; } = new();

    [JsonPropertyName("response")]
    public VcrResponse Response { get; set; } = new();

    [JsonPropertyName("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public sealed record VcrRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

public sealed record VcrResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("status_text")]
    public string StatusText { get; set; } = string.Empty;

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
}
