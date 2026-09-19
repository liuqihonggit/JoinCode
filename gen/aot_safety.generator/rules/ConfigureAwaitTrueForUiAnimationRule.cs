namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3014: UI 层禁止 ConfigureAwait 调用。仅 Ui 项目触发。
/// UI 层省略 ConfigureAwait 让默认行为（true）生效,显式 ConfigureAwait(true) 冗余,ConfigureAwait(false) 破坏 UI 线程亲和性。
/// </summary>
[AnalyzerRule(
    Id = "JCC3014",
    Title = "异步规范: UI 层禁止 ConfigureAwait 调用",
    Description = "UI 层（Gui/Tui 项目）中使用了 ConfigureAwait 调用. UI 层异步操作后续通常操作 UI 控件，必须在 UI 线程继续执行；省略 ConfigureAwait 让默认行为（ConfigureAwait(true)）生效即可. 显式 ConfigureAwait(true) 冗余，ConfigureAwait(false) 破坏 UI 线程亲和性.",
    Category = "AsyncCorrectness",
    Severity = DiagnosticSeverity.Error,
    HelpLinkUri = "UI layer (Gui/Tui) must not call ConfigureAwait at all. Omit it to let default ConfigureAwait(true) take effect. Explicit ConfigureAwait(true) is redundant; ConfigureAwait(false) breaks UI thread affinity.")]
public sealed class ConfigureAwaitTrueForUiAnimationRule : AnalyzerRuleBase<ConfigureAwaitTrueForUiAnimationRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (!projectContext.IsUi) return;
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.AwaitExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not AwaitExpressionSyntax awaitExpr) return;
        if (AotSafetyHelpers.IsTaskYield(awaitExpr)) return;

        if (AotSafetyHelpers.HasConfigureAwaitAny(awaitExpr)) {
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, awaitExpr.GetLocation()));
        }
    }
}
