
namespace JoinCode.Abstractions.Models.Ssh;

public sealed class SshCommandResult {
    /// <summary>获取执行的命令。</summary>
    public required string Command { get; init; }
    /// <summary>获取退出码。</summary>
    public required int ExitCode { get; init; }
    /// <summary>获取标准输出。</summary>
    public string Stdout { get; init; } = string.Empty;
    /// <summary>获取标准错误。</summary>
    public string Stderr { get; init; } = string.Empty;
    /// <summary>获取执行时长。</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>获取是否执行成功。</summary>
    public bool IsSuccess => ExitCode == 0;
}
