namespace JoinCode.Abstractions.Security.Sandbox.Ipc;

public sealed partial class SandboxIpcRequest
{
    public required string Type { get; init; }
    public required string RequestId { get; init; }
    public string? Payload { get; init; }
}

public sealed partial class SandboxIpcResponse
{
    public required string Type { get; init; }
    public required string RequestId { get; init; }
    public bool Success { get; init; }
    public string? Payload { get; init; }
    public string? Error { get; init; }
}

public sealed partial class SandboxExecuteRequest
{
    public required string Command { get; init; }
    public string? WorkingDirectory { get; init; }
    public int TimeoutMs { get; init; } = 30000;
    public Dictionary<string, string> EnvironmentVariables { get; init; } = [];
}

public sealed partial class SandboxExecuteResponse
{
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required int ExitCode { get; init; }
    public required bool Success { get; init; }
}
