
namespace Core.Bridge;

/// <summary>
/// Bridge 独立进程参数解析结果 — 对齐 TS 端 ParsedArgs
/// 解析 `jcc remote-control [options]` 命令行参数
/// </summary>
public sealed class BridgeMainArgs
{
    /// <summary>详细日志 — -v/--verbose</summary>
    public bool Verbose { get; init; }

    /// <summary>沙箱模式 — --sandbox/--no-sandbox</summary>
    public bool Sandbox { get; init; }

    /// <summary>调试文件路径 — --debug-file &lt;path&gt;</summary>
    public string? DebugFile { get; init; }

    /// <summary>会话超时（毫秒）— --session-timeout &lt;秒&gt; (内部*1000转毫秒)</summary>
    public int? SessionTimeoutMs { get; init; }

    /// <summary>权限模式 — --permission-mode &lt;mode&gt;</summary>
    public string? PermissionMode { get; init; }

    /// <summary>会话名称 — --name &lt;name&gt;</summary>
    public string? Name { get; init; }

    /// <summary>子进程生成模式 — --spawn &lt;session|same-dir|worktree&gt;</summary>
    public BridgeSpawnMode? SpawnMode { get; init; }

    /// <summary>最大并发会话数 — --capacity &lt;N&gt;</summary>
    public int? Capacity { get; init; }

    /// <summary>是否在目录中创建会话 — --[no-]create-session-in-dir</summary>
    public bool? CreateSessionInDir { get; init; }

    /// <summary>恢复会话 ID — --session-id &lt;id&gt;</summary>
    public string? SessionId { get; init; }

    /// <summary>继续上次会话 — -c/--continue</summary>
    public bool ContinueSession { get; init; }

    /// <summary>显示帮助 — -h/--help</summary>
    public bool Help { get; init; }

    /// <summary>解析错误信息</summary>
    public string? Error { get; init; }

    /// <summary>是否有错误</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);
}

/// <summary>
/// Bridge 独立进程参数解析器 — 委托给 BridgeCliArgParser 并映射为 BridgeMainArgs
/// </summary>
public static class BridgeMainArgsParser
{
    /// <summary>
    /// 解析命令行参数 — 对齐 TS 端 parseArgs(args: string[])
    /// </summary>
    /// <param name="args">命令行参数（不含子命令名本身）</param>
    /// <returns>解析结果</returns>
    public static BridgeMainArgs Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var result = BridgeCliArgParser.Parse(args);
        string? error = result.Error;

        int? sessionTimeoutMs = null;
        if (result.SessionTimeout is not null)
        {
            if (!int.TryParse(result.SessionTimeout, out var timeoutSec) || timeoutSec <= 0)
                error ??= "--session-timeout must be a positive integer (seconds)";
            else
                sessionTimeoutMs = timeoutSec * 1000;
        }

        BridgeSpawnMode? spawnMode = null;
        if (result.Spawn is not null)
        {
            spawnMode = result.Spawn switch
            {
                "session" => BridgeSpawnMode.SingleSession,
                "same-dir" => BridgeSpawnMode.SameDir,
                "worktree" => BridgeSpawnMode.Worktree,
                _ => null
            };
            if (spawnMode is null)
                error ??= $"--spawn must be one of: session, same-dir, worktree (got: {result.Spawn})";
        }

        int? capacity = null;
        if (result.Capacity is not null)
        {
            if (!int.TryParse(result.Capacity, out var cap) || cap <= 0)
                error ??= "--capacity must be a positive integer";
            else
                capacity = cap;
        }

        if (capacity.HasValue && spawnMode == BridgeSpawnMode.SingleSession)
            error ??= "--capacity cannot be used with --spawn=session";

        if ((result.SessionId is not null || result.Continue) &&
            (spawnMode.HasValue || capacity.HasValue || result.CreateSessionInDir.GetValueOrDefault()))
            error ??= "--session-id/--continue cannot be used with --spawn/--capacity/--create-session-in-dir";

        if (result.SessionId is not null && result.Continue)
            error ??= "--session-id and --continue are mutually exclusive";

        return new BridgeMainArgs
        {
            Verbose = result.Verbose,
            Sandbox = result.Sandbox ?? false,
            DebugFile = result.DebugFile,
            SessionTimeoutMs = sessionTimeoutMs,
            PermissionMode = result.PermissionMode,
            Name = result.Name,
            SpawnMode = spawnMode,
            Capacity = capacity,
            CreateSessionInDir = result.CreateSessionInDir,
            SessionId = result.SessionId,
            ContinueSession = result.Continue,
            Help = result.Help,
            Error = error,
        };
    }

    /// <summary>
    /// 生成帮助文本 — 对齐 TS 端 help 输出
    /// </summary>
    public static string GetHelpText() => BridgeCliArgParser.GetHelpText().Replace("bridgecliarg", "remote-control", StringComparison.OrdinalIgnoreCase);
}
