namespace Core.Permission;

/// <summary>
/// 工具列表权限中间件 — 检查自动批准/拒绝工具列表（规则粒度两档，TASK070 P2/DSG046）：
/// 工具级规则（无 RuleContent）与非命令内容规则（WebFetch domain 等 — 专用中间件兜底）→ 整工具决策（既有语义）；
/// 命令前缀规则（"Bash(npm run:*)"）→ 参数 command 前缀匹配才决策，未命中继续判定链落确认
/// （消除"前缀规则退化为整工具放行"的安全语义弱化 — 实证 Contains(toolName) 命中即放行全部 Bash）。
/// Default/Auto/Plan 模式下均生效。
/// </summary>
[Register(typeof(IPermissionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ToolListPermissionMiddleware : ServiceEntity, IPermissionMiddleware {
    /// <summary>命令前缀规则后缀（对齐 TS "npm run:*" — PermissionRuleParser 同语义）。</summary>
    private const string PrefixSuffix = ":*";

    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
        // 文件类工具带路径参数时不在此短路 — 工具级 allow 名单不得绕过路径级边界
        // (工作目录内外、敏感路径、deny 规则由 PathPermissionMiddleware 决策链判定；ADR 0138)
        if (context.Arguments is not null &&
            PermissionCheckContext.ExtractPathFromArguments(context.Arguments) is not null &&
            (PermissionCheckContext.IsFileReadTool(context.ToolName) || PermissionCheckContext.IsFileWriteTool(context.ToolName))) {
            return next(context, ct);
        }

        // 自动拒绝列表（deny 优先于 allow — 前缀规则 deny 同样参数匹配才拒绝）
        if (MatchesRule(context, context.AutoRejectedTools, context.Config.AutoRejectedTools)) {
            context.Result = ToolPermissionCheckResult.Rejected("工具在自动拒绝列表中");
            return Task.CompletedTask;
        }

        // 自动批准列表
        if (MatchesRule(context, context.AutoApprovedTools, context.Config.AutoApprovedTools)) {
            context.Result = ToolPermissionCheckResult.Approved();
            return Task.CompletedTask;
        }

        return next(context, ct);
    }

    /// <summary>
    /// 规则集匹配（TASK070 P2 — 两档粒度）：集合含工具名且（非前缀规则 或 前缀规则命中命令）→ true。
    /// 前缀规则未命中返回 false 继续判定链（落确认，不整工具放行）。
    /// </summary>
    private static bool MatchesRule(
        PermissionCheckContext context,
        HashSet<string> toolNames,
        Structura.Collections.ImmutableHamT<string, ToolPermissionRule> rules) {
        if (!toolNames.Contains(context.ToolName)) {
            return false;
        }

        var rule = rules.TryGetValue(context.ToolName, out var matched) ? matched : null;
        if (rule?.RuleContent is not { Length: > 0 } content || !content.EndsWith(PrefixSuffix, StringComparison.Ordinal)) {
            return true; // 工具级 / 非命令内容规则 — 整工具决策（既有语义；WebFetch domain 由专用中间件兜底）
        }

        // 命令前缀规则（"npm run:*"）— 参数 command 以 prefix 开头才决策（未命中继续判定链）
        var prefix = content[..^PrefixSuffix.Length];
        var command = PermissionRuleParser.ExtractCommand(context.Arguments);
        return PermissionRuleParser.MatchesContent(prefix, isPrefix: true, command);
    }
}
