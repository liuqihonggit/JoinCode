namespace Core.Hooks.Execution;

public sealed class HookDecision
{
    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("continue")]
    public bool? Continue { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("stopReason")]
    public string? StopReason { get; init; }
}
