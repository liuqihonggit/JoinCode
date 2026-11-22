
namespace Core.Permission;

/// <summary>
/// 分类器中间件 — Auto/Unattended 模式下使用工具过滤器和分类器判断权限
/// Unattended（含 autonomous 别名）只需硬规则拒绝，其余自动批准；Auto 保留灰区 ask
/// </summary>
[Register(typeof(IPermissionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class AutoClassifierMiddleware : ServiceEntity, IPermissionMiddleware {
    private readonly IToolPermissionFilter? _toolPermissionFilter;
    private readonly IAutoModeClassifier? _autoModeClassifier;
    private readonly ILlmAutoModeClassifier? _llmAutoModeClassifier;

    /// <summary>
    /// 创建 AutoClassifierMiddleware
    /// </summary>
    /// <param name="toolPermissionFilter">工具拒绝规则过滤器</param>
    /// <param name="autoModeClassifier">纯规则分类器（Stage 1 回退）</param>
    /// <param name="llmAutoModeClassifier">规则 + AI 灰区分类器（优先使用；可选，未注册时为 null）</param>
    public AutoClassifierMiddleware(
        IToolPermissionFilter? toolPermissionFilter = null,
        IAutoModeClassifier? autoModeClassifier = null,
        ILlmAutoModeClassifier? llmAutoModeClassifier = null) {
        _toolPermissionFilter = toolPermissionFilter;
        _autoModeClassifier = autoModeClassifier;
        _llmAutoModeClassifier = llmAutoModeClassifier;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
        // Auto / Unattended 模式使用分类器；Ask/Plan/Bypass 保持各自语义
        if (context.CurrentMode is not (PermissionMode.Auto or PermissionMode.Unattended)) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 文件类工具带路径参数时不在此短路 — 路径级边界（工作目录内外、敏感路径、deny 规则）
        // 必须由 PathPermissionMiddleware 的 9 步决策链判定，分类器只做与路径无关的粗分类
        // 修复缺陷（ADR 0138）: 此前对 SafeWrite/ReadOnly 直接 AutoApprove 并短路，
        // 导致 serve 等无确认环境可写任意非敏感 cwd 外路径（如 C:\Users\Public）
        if (context.Arguments is not null &&
            PermissionCheckContext.ExtractPathFromArguments(context.Arguments) is not null &&
            (PermissionCheckContext.IsFileReadTool(context.ToolName) || PermissionCheckContext.IsFileWriteTool(context.ToolName))) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // WebFetch 必须先走 WebFetchPermissionMiddleware 的域名级 deny/ask/allow 决策；
        // 分类器提前 AutoApprove 会绕过域名 deny 规则（Auto 与 Unattended 都适用）。
        if (PermissionCheckContext.IsWebFetchTool(context.ToolName)) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 工具拒绝规则过滤
        if (_toolPermissionFilter is not null && _toolPermissionFilter.IsToolDenied(context.ToolName)) {
            context.Result = ToolPermissionCheckResult.Rejected($"工具 '{context.ToolName}' 被拒绝规则过滤");
            return;
        }

        var llmClassifier = _llmAutoModeClassifier;
        var ruleClassifier = _autoModeClassifier;
        if (llmClassifier is null && ruleClassifier is null) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var request = new ClassificationRequest {
            ToolName = context.ToolName,
            Parameters = context.Arguments ?? new(),
            OperationType = OperationType.Auto
        };
        ClassificationResult classification;
        if (llmClassifier is not null && context.Config.LlmJudgeEnabled) {
            classification = await llmClassifier.ClassifyAsync(request, ct).ConfigureAwait(false);
        } else if (ruleClassifier is not null) {
            classification = await ruleClassifier.ClassifyAsync(request, ct).ConfigureAwait(false);
        } else {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        context.Result = context.CurrentMode == PermissionMode.Unattended
            ? MapUnattended(classification, context.ToolName)
            : MapForeground(classification, context.ToolName);
    }

    /// <summary>前台 Auto 映射 — 需要确认/审批时保留 ask 语义。</summary>
    private static ToolPermissionCheckResult MapForeground(ClassificationResult classification, string toolName)
        => classification.Action switch {
            SecurityAction.AutoApprove => ToolPermissionCheckResult.Approved(),
            SecurityAction.RequireConfirmation => ToolPermissionCheckResult.PendingConfirmation($"工具 '{toolName}' 需要确认: {classification.Reason}"),
            SecurityAction.RequireApproval => ToolPermissionCheckResult.PendingConfirmation($"工具 '{toolName}' 需要审批: {classification.Reason}"),
            SecurityAction.Block => ToolPermissionCheckResult.Rejected($"工具 '{toolName}' 被阻止: {classification.Reason}"),
            _ => ToolPermissionCheckResult.Approved()
        };

    /// <summary>Unattended 映射 — 没有宿主可确认，仅 Block 拒绝，其余自动执行（命令级硬红灯已在前层拦截 + 审计）。</summary>
    private static ToolPermissionCheckResult MapUnattended(ClassificationResult classification, string toolName)
        => classification.Action switch {
            SecurityAction.Block => ToolPermissionCheckResult.Rejected($"工具 '{toolName}' 被阻止: {classification.Reason}"),
            _ => ToolPermissionCheckResult.Approved()
        };
}