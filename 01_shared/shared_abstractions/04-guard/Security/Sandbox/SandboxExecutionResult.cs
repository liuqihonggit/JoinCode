namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxExecutionResult
{
    public required SandboxExecutionState State { get; init; }
    public required string ExecutionId { get; init; }
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public int? ExitCode { get; init; }
    public TimeSpan Elapsed { get; init; }
    public TimeSpan? ConfiguredTimeout { get; init; }
    public string? ErrorMessage { get; init; }

    public bool NeedsLlmDecision => State == SandboxExecutionState.TimedOut;

    public string GetLlmPrompt() => State switch
    {
        SandboxExecutionState.TimedOut => $"沙箱执行已超时（配置: {ConfiguredTimeout?.TotalMinutes:0}分钟, 已执行: {Elapsed.TotalSeconds:0}秒）。命令仍在运行中，未中断。请选择: 1) sandbox_exec_continue executionId={ExecutionId} action=wait 继续等待 2) sandbox_exec_continue executionId={ExecutionId} action=stop 强行终止",
        _ => string.Empty
    };
}
