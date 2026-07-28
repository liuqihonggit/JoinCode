namespace JoinCode.Abstractions.Interfaces.Lsp;

public interface ILspDiagnosticProvider
{
    List<(string ServerName, List<LspDiagnosticSummary> Files)> CheckPendingDiagnostics();
    void ClearDeliveredForFile(string fileUri);
}

public sealed class LspDiagnosticSummary
{
    public required string Uri { get; init; }
    public required List<LspDiagnosticEntry> Diagnostics { get; init; } = [];
}

public sealed class LspDiagnosticEntry
{
    public required string Message { get; init; }
    public string? Severity { get; init; }
    public int? StartLine { get; init; }
    public int? StartCharacter { get; init; }
    public string? Source { get; init; }
    public string? Code { get; init; }
}
