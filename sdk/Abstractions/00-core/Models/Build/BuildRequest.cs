namespace JoinCode.Abstractions.Models.Build;

/// <summary>
/// 编译队列请求
/// </summary>
public sealed record BuildRequest
{
    /// <summary>
    /// 原始编译命令
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// 工作目录
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 提交者 Agent 标识
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// 提交时间
    /// </summary>
    public DateTimeOffset SubmittedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 从 shell 命令解析编译请求
    /// </summary>
    public static BuildRequest Parse(string command, string? workingDirectory)
    {
        return new BuildRequest
        {
            Command = command,
            WorkingDirectory = workingDirectory,
        };
    }
}
