
namespace Core.Permission;

/// <summary>
/// WebFetch 域名权限中间件 — Default 模式下检查 WebFetch 域名级权限
/// 优先级: 预批准域名→deny规则→ask规则→allow规则(含RuleContent)→默认ask
/// 对齐 TS 版 WebFetchTool.checkPermissions
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class WebFetchPermissionMiddleware : IPermissionMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (context.CurrentMode != PermissionMode.Default || !PermissionCheckContext.IsWebFetchTool(context.ToolName))
            return next(context, ct);

        var webFetchResult = CheckWebFetchPermission(context.ToolName, context.Arguments, context.Config);
        if (webFetchResult is not null)
        {
            context.Result = webFetchResult;
            return Task.CompletedTask;
        }

        return next(context, ct);
    }

    /// <summary>
    /// WebFetch 域名级权限检查 — 对齐 TS 版 WebFetchTool.checkPermissions
    /// </summary>
    private static ToolPermissionCheckResult? CheckWebFetchPermission(string toolName, Dictionary<string, JsonElement>? arguments, PermissionConfig config)
    {
        if (arguments == null || !arguments.TryGetValue("url", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
            return null;

        var url = urlEl.GetString()!;

        // 1. 预批准域名检查
        if (PreapprovedDomains.IsPreapprovedUrl(url))
            return ToolPermissionCheckResult.Approved();

        var ruleContent = ExtractWebFetchRuleContent(url);

        // 2. deny 规则匹配
        if (MatchesWebFetchRule(config.AutoRejectedTools, ruleContent))
            return ToolPermissionCheckResult.Rejected($"WebFetch denied access to {ruleContent}.");

        // 3. ask 规则匹配
        if (MatchesWebFetchRule(config.AskRules, ruleContent))
            return ToolPermissionCheckResult.PendingConfirmation(
                $"Claude requested permissions to use {toolName}, but you haven't granted it yet.");

        // 4. allow 规则匹配（含 RuleContent 的细粒度规则）
        if (MatchesWebFetchRuleWithContent(config.AutoApprovedTools, ruleContent))
            return ToolPermissionCheckResult.Approved();

        // 5. 默认 → ask
        return ToolPermissionCheckResult.PendingConfirmation(
            $"Claude requested permissions to use {toolName}, but you haven't granted it yet.");
    }

    /// <summary>
    /// 提取 WebFetch ruleContent — 格式: "domain:{hostname}"
    /// </summary>
    private static string ExtractWebFetchRuleContent(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                return $"domain:{parsed.Host}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to parse URL '{url}': {ex.Message}");
        }

        return $"input:{url}";
    }

    /// <summary>
    /// 检查规则列表中是否有匹配的 RuleContent
    /// </summary>
    private static bool MatchesWebFetchRule(List<ToolPermissionRule> rules, string ruleContent)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!string.Equals(rule.ToolName, WebToolNameConstants.WebFetch, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrEmpty(rule.RuleContent))
                continue;
            if (string.Equals(rule.RuleContent, ruleContent, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 检查 allow 规则列表中是否有匹配的 RuleContent
    /// 无 RuleContent 的 WebFetch 规则不匹配（避免无条件批准所有域名）
    /// </summary>
    private static bool MatchesWebFetchRuleWithContent(List<ToolPermissionRule> rules, string ruleContent)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!string.Equals(rule.ToolName, WebToolNameConstants.WebFetch, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrEmpty(rule.RuleContent))
                continue;
            if (string.Equals(rule.RuleContent, ruleContent, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
