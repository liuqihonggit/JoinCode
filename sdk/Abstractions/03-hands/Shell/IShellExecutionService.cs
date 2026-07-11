
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Shell 执行服务接口
/// </summary>
public interface IShellExecutionService
{
    /// <summary>
    /// 执行 Shell 命令
    /// </summary>
    /// <param name="command">命令</param>
    /// <param name="timeout">超时时间（毫秒）</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="disableSandbox">跳过沙箱路径解析 — 对齐 TS dangerouslyDisableSandbox</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<ShellExecutionResult> ExecuteAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行 PowerShell 命令
    /// </summary>
    /// <param name="command">命令</param>
    /// <param name="timeout">超时时间（毫秒）</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="disableSandbox">跳过沙箱路径解析 — 对齐 TS dangerouslyDisableSandbox</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<ShellExecutionResult> ExecutePowerShellAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动可后台化的 Shell 命令 — 对齐 TS ShellCommand.exec
    /// 返回 IShellCommandContext，支持超时自动后台化、Assistant 自动后台化、用户手动后台化
    /// </summary>
    /// <param name="command">命令</param>
    /// <param name="timeout">超时时间（毫秒）</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="isPowerShell">是否使用 PowerShell</param>
    /// <param name="shouldAutoBackground">是否允许超时自动后台化 — 对齐 TS shouldAutoBackground</param>
    /// <param name="disableSandbox">跳过沙箱路径解析 — 对齐 TS dangerouslyDisableSandbox</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>可后台化的执行上下文</returns>
    Task<IShellCommandContext> StartWithBackgroundSupportAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool isPowerShell = false,
        bool shouldAutoBackground = true,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default);
}
