namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6009: 代码规范 — 禁止 Parallel.For/ForEach/ForEachAsync，使用 AsParallel() 或 Task.WhenAll 替代。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6009",
    Title = "代码规范: 禁止 Parallel.For/ForEach/ForEachAsync，使用 AsParallel() 或 Task.WhenAll 替代",
    Description = "Parallel.For/ForEach/ForEachAsync 不符合 LINQ 链式编程风格。使用 AsParallel() PLINQ 链式或 Task.WhenAll 替代。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Parallel 的问题: 1) 命令式风格，不符合 LINQ 链式编程; 2) 异常以 AggregateException 抛出, 难以定位根因; 3) 共享状态需要加锁, 易死锁; 4) 不支持 async/await. 替代方案: 1) PLINQ 链式: items.AsParallel().WithDegreeOfParallelism(n).Select(x => Process(x)).ToList(); 2) 异步并发: await Task.WhenAll(items.Select(x => ProcessAsync(x))); 3) 限流并发: await Parallel.ForEachAsync(items, options, async (item, ct) => ...).")]
public sealed class ParallelForEachRule : AnalyzerRuleBase<ParallelForEachRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeParallelForEach, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeParallelForEach(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        if (ctx.Node is not InvocationExpressionSyntax invocation) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (methodName != "ForEach" && methodName != "ForEachAsync" && methodName != "For") return;

        if (memberAccess.Expression is not IdentifierNameSyntax className) return;
        if (className.Identifier.ValueText != "Parallel") return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation()));
    }
}
