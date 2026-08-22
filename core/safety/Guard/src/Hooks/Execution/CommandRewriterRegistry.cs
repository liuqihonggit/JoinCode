namespace Core.Hooks.Execution;

/// <summary>
/// 命令改写器注册表 — 管理所有改写器，按优先级执行
/// <para>
/// 核心价值：
/// 1. 统一管理所有命令改写器
/// 2. 按优先级执行改写
/// 3. 记录改写日志
/// </para>
/// </summary>
[Register]
public sealed partial class CommandRewriterRegistry : ServiceEntity
{
    private readonly List<ICommandRewriter> _rewriters = [];
    [Inject] private readonly ILogger<CommandRewriterRegistry>? _logger;
    private readonly INetworkConnectivityService? _networkService;

    public CommandRewriterRegistry(ILogger<CommandRewriterRegistry>? logger = null, INetworkConnectivityService? networkService = null)
    {
        _logger = logger;
        _networkService = networkService;
        RegisterDefaultRewriters();
    }

    /// <summary>
    /// 注册默认改写器 — GhPrBodyRewriter + GhTimeoutRewriter + VpnRouteRewriter
    /// </summary>
    private void RegisterDefaultRewriters()
    {
        Register(new Rewriters.HeredocRewriter());
        Register(new Rewriters.GhPrBodyRewriter());
        Register(new Rewriters.GhTimeoutRewriter());
        Register(new Rewriters.VpnRouteRewriter(networkService: _networkService));
    }

    /// <summary>
    /// 注册改写器
    /// </summary>
    public void Register(ICommandRewriter rewriter)
    {
        ArgumentNullException.ThrowIfNull(rewriter);
        _rewriters.Add(rewriter);
        _logger?.LogDebug("注册命令改写器: {Name} (优先级: {Priority})", rewriter.Name, rewriter.Priority);
    }

    /// <summary>
    /// 改写命令 — 按优先级从高到低执行，第一个匹配的改写器生效
    /// </summary>
    public CommandRewriteResult Rewrite(string command, IReadOnlyDictionary<string, object>? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var ctx = context ?? EmptyContext;

        foreach (var rewriter in _rewriters.OrderByDescending(static r => r.Priority))
        {
            if (!rewriter.CanRewrite(command)) continue;

            var rewritten = rewriter.Rewrite(command, ctx);
            if (rewritten == command) continue;

            _logger?.LogInformation(
                "命令已改写: {Original} → {Rewritten} (改写器: {Name})",
                command,
                rewritten,
                rewriter.Name);

            return new CommandRewriteResult
            {
                OriginalCommand = command,
                RewrittenCommand = rewritten,
                WasRewritten = true,
                RewriterName = rewriter.Name,
                Reason = $"由 {rewriter.Name} 改写"
            };
        }

        return new CommandRewriteResult
        {
            OriginalCommand = command,
            RewrittenCommand = command,
            WasRewritten = false
        };
    }

    /// <summary>
    /// 获取所有已注册的改写器
    /// </summary>
    public IReadOnlyList<ICommandRewriter> GetRewriters() => _rewriters;

    private static readonly IReadOnlyDictionary<string, object> EmptyContext =
        FrozenDictionary<string, object>.Empty;
}
