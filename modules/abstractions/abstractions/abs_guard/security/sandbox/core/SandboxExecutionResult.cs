namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxExecutionResult {
    /// <summary>获取执行状态。</summary>
    public required SandboxExecutionState State { get; init; }
    /// <summary>获取执行标识。</summary>
    public required string ExecutionId { get; init; }
    /// <summary>获取标准输出。</summary>
    public string? Stdout { get; init; }
    /// <summary>获取标准错误。</summary>
    public string? Stderr { get; init; }
    /// <summary>获取退出码。</summary>
    public int? ExitCode { get; init; }
    /// <summary>获取执行耗时。</summary>
    public TimeSpan Elapsed { get; init; }
    /// <summary>获取配置的超时时间。</summary>
    public TimeSpan? ConfiguredTimeout { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>获取是否需要 LLM 决策（超时状态时为 true）。</summary>
    public bool NeedsLlmDecision => State == SandboxExecutionState.TimedOut;

    /// <summary>获取供 LLM 决策的提示文本。</summary>
    public string GetLlmPrompt() => State switch {
        SandboxExecutionState.TimedOut => $"沙箱执行已超时（配置: {ConfiguredTimeout?.TotalMinutes:0}分钟, 已执行: {Elapsed.TotalSeconds:0}秒）。命令仍在运行中，未中断。请选择: 1) sandbox_exec_continue executionId={ExecutionId} action=wait 继续等待 2) sandbox_exec_continue executionId={ExecutionId} action=stop 强行终止",
        _ => string.Empty
    };
}