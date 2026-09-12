namespace JoinCode.Abstractions.Security.Sandbox;

public sealed class ProviderExecutionResult
{
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required int ExitCode { get; init; }
    public required bool Success { get; init; }
    public required bool TimedOut { get; init; }
}
