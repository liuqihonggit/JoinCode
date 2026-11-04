namespace JoinCode.Abstractions.Security.Sandbox.Ipc;

public sealed partial class SandboxIpcRequest {
    /// <summary>获取请求类型。</summary>
    public required string Type { get; init; }
    /// <summary>获取请求标识。</summary>
    public required string RequestId { get; init; }
    /// <summary>获取请求负载。</summary>
    public string? Payload { get; init; }
}

public sealed partial class SandboxIpcResponse {
    /// <summary>获取响应类型。</summary>
    public required string Type { get; init; }
    /// <summary>获取对应请求标识。</summary>
    public required string RequestId { get; init; }
    /// <summary>获取是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>获取响应负载。</summary>
    public string? Payload { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? Error { get; init; }
}

public sealed partial class SandboxExecuteRequest {
    /// <summary>获取要执行的命令。</summary>
    public required string Command { get; init; }
    /// <summary>获取工作目录。</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>获取超时时间(毫秒)。</summary>
    public int TimeoutMs { get; init; } = 30000;
    /// <summary>获取环境变量字典。</summary>
    public Dictionary<string, string> EnvironmentVariables { get; init; } = [];
}

public sealed partial class SandboxExecuteResponse {
    /// <summary>获取标准输出。</summary>
    public required string StandardOutput { get; init; }
    /// <summary>获取标准错误。</summary>
    public required string StandardError { get; init; }
    /// <summary>获取退出码。</summary>
    public required int ExitCode { get; init; }
    /// <summary>获取是否成功。</summary>
    public required bool Success { get; init; }
}