namespace Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// GitHub PR Body 守卫 — 为 gh pr create 自动补全 --body 参数(迁移自旧 GhPrBodyRewriter)
/// <para>
/// 解决问题:LLM 调用 gh pr create 时经常忘记写 body,导致 PR 描述为空。
/// 检测到缺 --body 时返回 <see cref="CommandDecision.Rewrite"/> 补全参数。
/// </para>
/// <para>
/// 迁移自 GhPrBodyRewriter(Priority=100)。
/// </para>
/// </summary>
[Register(typeof(ICommandGuard), ServiceLifetime.Singleton)]
public sealed partial class GhPrBodyGuard : ICommandGuard
{
    private readonly ILogger<GhPrBodyGuard>? _logger;

    /// <summary>
    /// 构造 gh pr body 守卫
    /// </summary>
    /// <param name="logger">日志器(可选)</param>
    public GhPrBodyGuard(ILogger<GhPrBodyGuard>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "GhPrBodyGuard";

    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public bool CanHandle(string command, GuardContext context)
    {
        var normalized = command.TrimStart();
        return normalized.StartsWith("gh pr create", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("gh.exe pr create", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public CommandDecision Evaluate(string command, GuardContext context)
    {
        if (HasBodyParameter(command))
        {
            return new CommandDecision.Allow();
        }

        var body = GetBodyFromContext(context);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = GenerateDefaultBody(context);
        }

        var escapedBody = EscapeBody(body);
        var rewritten = $"{command} --body \"{escapedBody}\"";

        _logger?.LogInformation("为 gh pr create 自动添加 --body 参数");

        return new CommandDecision.Rewrite(rewritten, "补全 --body 参数");
    }

    /// <summary>
    /// 检查命令是否已包含 --body 参数
    /// </summary>
    private static bool HasBodyParameter(string command)
    {
        return command.Contains("--body", StringComparison.OrdinalIgnoreCase)
               || command.Contains("-b ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从上下文获取 body 内容
    /// </summary>
    private static string? GetBodyFromContext(GuardContext context)
    {
        return context.PrBody;
    }

    /// <summary>
    /// 生成默认 body 模板
    /// </summary>
    private static string GenerateDefaultBody(GuardContext context)
    {
        var title = context.PrTitle ?? "变更内容";
        var branch = context.HeadBranch is not null ? $"分支: `{context.HeadBranch}`" : null;
        var description = branch;
        return IO.ProcessService.PrBodyGenerator.GenerateWithTemplate(title, description);
    }

    /// <summary>
    /// 转义 body 内容中的特殊字符
    /// </summary>
    private static string EscapeBody(string body)
    {
        return body
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }
}
