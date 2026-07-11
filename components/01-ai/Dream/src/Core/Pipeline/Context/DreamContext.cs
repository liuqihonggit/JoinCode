namespace JoinCode.Dream.Pipeline;

using JoinCode.Abstractions.Pipeline;

public sealed class DreamContext : IPipelineContext
{
    public required DreamRequest Request { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public IReadOnlyList<string> SessionIds { get; set; } = [];
    public string? TaskId { get; set; }
    public string? SystemPrompt { get; set; }
    public string? UserPrompt { get; set; }
    public string? ConsolidationResult { get; set; }
    public DreamResult? Result { get; set; }

    public bool GateChecked { get; set; }
    public bool SessionsScanned { get; set; }
    public bool TaskRegistered { get; set; }
    public bool PromptBuilt { get; set; }
    public bool LlmCompleted { get; set; }
    public bool TurnRecorded { get; set; }
    public bool TaskCompleted { get; set; }

    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }
    public void Fail(string message) { Failed = true; ErrorMessage = message; }
}
