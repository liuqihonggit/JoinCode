namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3009: 测试代码禁止 ConfigureAwait(false)。仅 Test 项目触发。
/// </summary>
[AnalyzerRule(
    Id = "JCC3009",
    Title = "异步规范: 测试代码禁止 ConfigureAwait(false)",
    Description = "测试代码中使用了 ConfigureAwait(false). 测试代码依赖 xUnit SynchronizationContext，ConfigureAwait(false) 会导致测试行为不一致. 测试代码中 await 默认即为 ConfigureAwait(true)，无需显式指定.",
    Category = "AsyncCorrectness",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "Test code must not use ConfigureAwait(false). Default ConfigureAwait(true) is implicit, no need to specify explicitly.")]
public sealed class ConfigureAwaitTrueForTestsRule : AnalyzerRuleBase<ConfigureAwaitTrueForTestsRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (!projectContext.IsTest) return;
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.AwaitExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not AwaitExpressionSyntax awaitExpr) return;
        if (AotSafetyHelpers.IsTaskYield(awaitExpr)) return;

        if (AotSafetyHelpers.HasConfigureAwaitFalse(awaitExpr)) {
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, awaitExpr.GetLocation()));
        }
    }
}
