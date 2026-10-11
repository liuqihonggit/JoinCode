namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC5001: 性能 — Thread.Sleep 阻塞线程。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC5001",
    Title = "性能: Thread.Sleep 阻塞线程",
    Description = "Thread.Sleep 在非测试代码中使用，会阻塞当前线程。应使用 Task.Delay 替代，不阻塞线程池线程。",
    Category = "PerformanceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Thread.Sleep 阻塞当前线程，在异步应用中浪费线程池资源.Task.Delay 是异步替代方案，不阻塞线程.测试代码中可以使用 Thread.Sleep 但应标记为 [Fact(Timeout = N)] 防止无限等待.")]
public sealed class ThreadSleepRule : AnalyzerRuleBase<ThreadSleepRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeThreadSleep, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeThreadSleep(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var invocation = (InvocationExpressionSyntax)ctx.Node;

        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType;
        if (containingType is null) return;

        if (containingType.Name != "Thread" || symbol.Name != "Sleep") return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation()));
    }
}
