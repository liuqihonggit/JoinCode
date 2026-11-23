
namespace Core.Permission;

/// <summary>
/// WebFetch 域名权限中间件 — Auto/Unattended 模式下检查 WebFetch 域名级权限
/// 优先级: 预批准域名→deny规则→ask规则→allow规则(含RuleContent)→默认ask
/// 对齐 TS 版 WebFetchTool.checkPermissions
/// </summary>
[Register(typeof(IPermissionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WebFetchPermissionMiddleware : ServiceEntity, IPermissionMiddleware {
    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
        if (context.CurrentMode is not (PermissionMode.Auto or PermissionMode.Unattended) || !PermissionCheckContext.IsWebFetchTool(context.ToolName))
            return next(context, ct);

        var webFetchResult = CheckWebFetchPermission(context);
        if (webFetchResult is not null) {
            context.Result = webFetchResult;
            return Task.CompletedTask;
        }

        return next(context, ct);
    }

    /// <summary>
    /// WebFetch 域名级权限检查 — 对齐 TS 版 WebFetchTool.checkPermissions
    /// </summary>

    private static ToolPermissionCheckResult? CheckWebFetchPermission(PermissionCheckContext context) {
        var toolName = context.ToolName;
        var arguments = context.Arguments;
        var config = context.Config;

        if (arguments == null || !arguments.TryGetValue("url", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
            return null;

        var url = urlEl.GetString();
        if (url is null) return null;

        // 1. 预批准域名检查
        if (PreapprovedDomains.IsPreapprovedUrl(url))
            return ToolPermissionCheckResult.Approved();

        var ruleContent = ExtractWebFetchRuleContent(url);
        var unattended = context.CurrentMode == PermissionMode.Unattended;

        // 2. deny 规则匹配 — 任何模式都必须拒绝
        if (MatchesWebFetchRule(config.AutoRejectedTools.Values, ruleContent))
            return ToolPermissionCheckResult.Rejected($"WebFetch denied access to {ruleContent}.");

        // 3. ask 规则匹配 — Unattended 无宿主可确认，按只读网络访问自动批准（硬 deny 仍在上面）
        if (MatchesWebFetchRule(config.AskRules, ruleContent))
            return unattended
                ? ToolPermissionCheckResult.Approved()
                : ToolPermissionCheckResult.PendingConfirmation(
                    $"{BrandConstants.ProductName} requested permissions to use {toolName}, but you haven't granted it yet.");

        // 4. allow 规则匹配（含 RuleContent 的细粒度规则）
        if (MatchesWebFetchRuleWithContent(config.AutoApprovedTools.Values, ruleContent))
            return ToolPermissionCheckResult.Approved();

        // 5. 默认 — Unattended 自动批准只读 WebFetch；前台保持 ask
        return unattended
            ? ToolPermissionCheckResult.Approved()
            : ToolPermissionCheckResult.PendingConfirmation(
                $"JoinCode requested permissions to use {toolName}, but you haven't granted it yet.");
    }

    /// <summary>
    /// 提取 WebFetch ruleContent — 格式: "domain:{hostname}"
    /// </summary>
    private static string ExtractWebFetchRuleContent(string url, ILogger? logger = null) {
        try {
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                return $"domain:{parsed.Host}";
        } catch (Exception ex) {
            logger?.LogWarning(ex, "Failed to parse URL '{Url}'", url);
        }

        return $"input:{url}";
    }

    /// <summary>
    /// 检查规则列表中是否有匹配的 RuleContent
    /// </summary>
    private static bool MatchesWebFetchRule(IEnumerable<ToolPermissionRule> rules, string ruleContent) {
        foreach (var rule in rules) {
            if (!string.Equals(rule.ToolName, WebToolNameEnumConstants.WebFetch, StringComparison.OrdinalIgnoreCase))
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
    private static bool MatchesWebFetchRuleWithContent(IEnumerable<ToolPermissionRule> rules, string ruleContent) {
        foreach (var rule in rules) {
            if (!string.Equals(rule.ToolName, WebToolNameEnumConstants.WebFetch, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrEmpty(rule.RuleContent))
                continue;
            if (string.Equals(rule.RuleContent, ruleContent, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}