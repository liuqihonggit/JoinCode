namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC4003: 死锁风险 — lock 语句内调用 GetAwaiter().GetResult()。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC4003",
    Title = "死锁风险: lock 语句内调用 GetAwaiter().GetResult()",
    Description = "lock 语句内调用 '{0}.GetAwaiter().GetResult()' 可能导致死锁. 如果异步操作需要回到被 lock 阻塞的线程，将形成死锁. 应使用 SemaphoreSlim.WaitAsync() 替代 lock + GetAwaiter 模式.",
    Category = "DeadlockSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "lock 持有线程独占锁，GetAwaiter().GetResult() 阻塞当前线程等待异步操作完成.如果异步操作需要获取同一锁或回到被阻塞的线程(SynchronizationContext)，将形成死锁.正确模式: SemaphoreSlim(1,1) + WaitAsync + try/finally Release.")]
public sealed class LockWithGetAwaiterRule : AnalyzerRuleBase<LockWithGetAwaiterRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeLockWithGetAwaiter, SyntaxKind.LockStatement);
    }

    private static void AnalyzeLockWithGetAwaiter(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var lockStatement = (LockStatementSyntax)ctx.Node;

        var getAwaiterInvocations = lockStatement.Statement.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => IsGetAwaiterGetResultPattern(inv));

        foreach (var invocation in getAwaiterInvocations) {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var exprText = invocation.Expression.ToString();
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation(), exprText));
        }
    }

    private static bool IsGetAwaiterGetResultPattern(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is not MemberAccessExpressionSyntax outerAccess) return false;
        if (outerAccess.Name.Identifier.ValueText != "GetResult") return false;

        if (outerAccess.Expression is not InvocationExpressionSyntax innerInvocation) return false;
        if (innerInvocation.Expression is not MemberAccessExpressionSyntax innerAccess) return false;
        if (innerAccess.Name.Identifier.ValueText != "GetAwaiter") return false;

        return true;
    }
}
