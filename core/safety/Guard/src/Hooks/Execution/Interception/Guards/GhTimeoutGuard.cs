namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// gh 命令超时守卫 — 记录 gh 命令日志,放行(对齐 <see cref="Core.Hooks.Execution.Rewriters.GhTimeoutRewriter"/>)
/// <para>
/// 迁移自 GhTimeoutRewriter(Priority=50)。原改写器仅记录日志不改写,此处保留行为。
/// </para>
/// </summary>
[Register]
public sealed partial class GhTimeoutGuard : ICommandGuard
{
    [Inject] private readonly ILogger<GhTimeoutGuard>? _logger;

    /// <summary>
    /// 构造 gh 超时守卫
    /// </summary>
    /// <param name="logger">日志器(可选)</param>
    public GhTimeoutGuard(ILogger<GhTimeoutGuard>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "GhTimeoutGuard";

    /// <inheritdoc/>
    public int Priority => 50;

    /// <inheritdoc/>
    public bool CanHandle(string command, IReadOnlyDictionary<string, object> context)
    {
        var normalized = command.TrimStart();
        return normalized.StartsWith("gh ", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("gh.exe ", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, IReadOnlyDictionary<string, object> context)
    {
        _logger?.LogDebug("gh 命令超时控制: {Command}", command);
        return new CommandDecision.Allow();
    }
}
