namespace Core.Agents;

/// <summary>
/// Git 命令执行结果
/// </summary>
public sealed class GitCommandResult
{
    public required bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}
