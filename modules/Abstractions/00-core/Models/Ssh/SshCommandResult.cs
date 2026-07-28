
namespace JoinCode.Abstractions.Models.Ssh;

public sealed class SshCommandResult
{
    public required string Command { get; init; }
    public required int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool IsSuccess => ExitCode == 0;
}
