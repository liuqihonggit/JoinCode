namespace Tools.Shell;

/// <summary>
/// Shell 路径门控中间件 — 根据当前平台和目标 Shell 类型转换路径格式
/// 确保 LLM 输出的路径在传递给执行层之前已转换为正确格式：
///   - Windows + Bash(Git Bash/WSL) → POSIX 格式
///   - Windows + PowerShell/Cmd/Python → Windows 格式
///   - Linux/Mac → POSIX 格式
/// 覆盖 working_directory 和 command 中的路径片段
/// UNC 路径 + Bash 组合时记录警告（Git Bash 对 UNC 支持有限）
/// </summary>
[Register]
public sealed partial class ShellPathGateMiddleware : IShellMiddleware
{
    [Inject] private readonly IEnvironmentProbeService _probeService;
    [Inject] private readonly ILogger<ShellPathGateMiddleware>? _logger;

    /// <inheritdoc />
    public Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(context.WorkingDirectory))
        {
            var gated = _probeService.GatePath(context.WorkingDirectory, context.Provider);
            if (!string.Equals(gated, context.WorkingDirectory, StringComparison.Ordinal))
            {
                context.WorkingDirectory = gated;
            }

            WarnUncPathForBash(context.WorkingDirectory, context.Provider);
        }

        if (!string.IsNullOrEmpty(context.Command))
        {
            var gatedCommand = _probeService.GateCommandPaths(context.Command, context.Provider);
            if (!string.Equals(gatedCommand, context.Command, StringComparison.Ordinal))
            {
                context.Command = gatedCommand;
            }
        }

        return next(context, ct);
    }

    private void WarnUncPathForBash(string path, IShellProvider provider)
    {
        if (provider.Type != ShellType.Bash) return;
        if (!PathConverter.LooksLikeWindowsPath(path) && !path.StartsWith("//")) return;

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("//"))
        {
            _logger?.LogWarning("[ShellPathGate] UNC path '{Path}' used with Bash — Git Bash UNC support is limited, consider mapping to a local drive", path);
        }
    }
}
