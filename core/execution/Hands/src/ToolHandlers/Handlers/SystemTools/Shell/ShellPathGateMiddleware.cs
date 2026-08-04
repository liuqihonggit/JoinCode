namespace Tools.Shell;

/// <summary>
/// Shell 路径门控中间件 — 根据当前平台和目标 Shell 类型转换路径格式
/// 确保 LLM 输出的路径在传递给执行层之前已转换为正确格式：
///   - Windows + Bash(Git Bash/WSL) → POSIX 格式
///   - Windows + PowerShell → Windows 格式
///   - Linux/Mac → POSIX 格式
/// 覆盖 working_directory 和 command 中的路径片段
/// </summary>
[Register]
public sealed partial class ShellPathGateMiddleware : IShellMiddleware
{
    [Inject] private readonly IEnvironmentProbeService _probeService;

    /// <inheritdoc />
    public Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(context.WorkingDirectory))
        {
            var gated = _probeService.GatePath(context.WorkingDirectory, context.IsPowerShell);
            if (!string.Equals(gated, context.WorkingDirectory, StringComparison.Ordinal))
            {
                context.WorkingDirectory = gated;
            }
        }

        if (!string.IsNullOrEmpty(context.Command))
        {
            var gatedCommand = _probeService.GateCommandPaths(context.Command, context.IsPowerShell);
            if (!string.Equals(gatedCommand, context.Command, StringComparison.Ordinal))
            {
                context.Command = gatedCommand;
            }
        }

        return next(context, ct);
    }
}
