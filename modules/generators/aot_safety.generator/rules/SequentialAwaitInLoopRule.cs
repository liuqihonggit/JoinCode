namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3007: 循环中逐个 await 可考虑改为 Task.WhenAll 并发执行。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3007",
    Title = "异步性能: 循环中逐个 await 可考虑改为 Task.WhenAll 并发执行",
    Description = "循环体内逐个 await 异步操作是串行执行，可考虑收集 Task 后用 Task.WhenAll 并发执行. 串行 await 总耗时 = Σ每个操作耗时，并发 await 总耗时 ≈ Max(各操作耗时).",
    Category = "AsyncPerformance",
    Severity = DiagnosticSeverity.Info,
    HelpLinkUri = "Sequential await in loop could use Task.WhenAll for concurrent execution. Serial await total time = sum of each operation, concurrent await total time = max of each operation.")]
public sealed class SequentialAwaitInLoopRule : AnalyzerRuleBase<SequentialAwaitInLoopRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.AwaitExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not AwaitExpressionSyntax awaitExpr) return;

        if (!AotSafetyHelpers.IsInsideLoop(awaitExpr)) return;
        if (awaitExpr.Parent is not ExpressionStatementSyntax) return;

        var loop = AotSafetyHelpers.FindInnermostLoop(awaitExpr);
        if (loop is null) return;
        if (loop is WhileStatementSyntax or DoStatementSyntax) return;

        if (loop is ForStatementSyntax forStmt) {
            if (AotSafetyHelpers.ContainsCancellationTokenCondition(forStmt.Condition)) return;
        }

        if (AotSafetyHelpers.LoopHasEarlyExit(loop)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, awaitExpr.GetLocation()));
    }
}
