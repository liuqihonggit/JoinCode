namespace Tools.Shell;

/// <summary>
/// Shell 搜索命令超时中间件 — 对搜索类命令（rg/grep/find/ag 等）缩短默认超时
/// 防止搜索范围意外过大时长时间卡顿
/// 仅在用户未显式指定超时时生效
/// </summary>
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellSearchTimeoutMiddleware : ServiceEntity, IShellMiddleware
{
    private static readonly FrozenSet<string> SearchCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "rg", "grep", "egrep", "fgrep", "ag", "ack",
        "find", "fd", "fdfind", "locate", "mlocate");

    private readonly ShellExecutionConfig _config;

    public ShellSearchTimeoutMiddleware(ShellExecutionConfig config)
    {
        _config = config;
    }

    public Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        if (IsSearchCommand(context.Command) && context.Timeout is null or > 30_000)
        {
            var searchTimeoutMs = _config.SearchCommandTimeoutSeconds * 1000;
            context.OverrideTimeout = searchTimeoutMs;
        }

        return next(context, ct);
    }

    private static bool IsSearchCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var spaceIdx = command.IndexOf(' ');
        var cmdName = spaceIdx >= 0 ? command[..spaceIdx] : command;

        if (SearchCommands.Contains(cmdName))
        {
            return true;
        }

        var slashIdx = cmdName.LastIndexOf('/');
        if (slashIdx >= 0 && slashIdx < cmdName.Length - 1)
        {
            var basename = cmdName[(slashIdx + 1)..];
            return SearchCommands.Contains(basename);
        }

        return false;
    }
}
