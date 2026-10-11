namespace JoinCode.Abstractions.Security.Sandbox;

public sealed class ProviderExecutionResult {
    /// <summary>获取标准输出。</summary>
    public required string StandardOutput { get; init; }
    /// <summary>获取标准错误。</summary>
    public required string StandardError { get; init; }
    /// <summary>获取退出码。</summary>
    public required int ExitCode { get; init; }
    /// <summary>获取一个值，指示执行是否成功。</summary>
    public required bool Success { get; init; }
    /// <summary>获取一个值，指示是否超时。</summary>
    public required bool TimedOut { get; init; }
}