namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3008: 库代码 await 必须使用 ConfigureAwait(false)。仅 Library 项目触发。
/// </summary>
[AnalyzerRule(
    Id = "JCC3008",
    Title = "异步规范: 库代码 await 必须使用 ConfigureAwait(false)",
    Description = "库代码（lib/ 和 subsystems/）中的 await 缺少 ConfigureAwait(false). 库代码不依赖 SynchronizationContext，省略 ConfigureAwait(false) 会导致不必要的上下文切换和潜在死锁.",
    Category = "AsyncCorrectness",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "Library code (lib/ and subsystems/) must use ConfigureAwait(false). Host entry (JoinCode) and test code are exempt.")]
public sealed class ConfigureAwaitFalseRule : AnalyzerRuleBase<ConfigureAwaitFalseRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (!projectContext.IsLibrary) return;
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.AwaitExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not AwaitExpressionSyntax awaitExpr) return;
        if (AotSafetyHelpers.HasConfigureAwaitFalse(awaitExpr)) return;
        if (AotSafetyHelpers.IsTaskYield(awaitExpr)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, awaitExpr.GetLocation()));
    }
}
