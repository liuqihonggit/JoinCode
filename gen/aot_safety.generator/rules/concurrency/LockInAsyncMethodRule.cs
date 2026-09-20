namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC4001: 死锁风险 — lock 语句在 async 方法中使用。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC4001",
    Title = "死锁风险: lock 语句在 async 方法中使用",
    Description = "lock 语句在 async 方法 '{0}' 中使用. lock 不支持 await，如果持有锁的线程需要 await 另一个需要同一锁的操作，将导致死锁. 应使用 SemaphoreSlim(1, 1) 替代.",
    Category = "DeadlockSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "lock 语句在 async 上下文中是危险的: 1) lock 块内不能使用 await; 2) 如果持有锁的代码路径间接 await 了需要同一锁的操作，会死锁; 3) SemaphoreSlim.WaitAsync 是异步兼容的替代方案.")]
public sealed class LockInAsyncMethodRule : AnalyzerRuleBase<LockInAsyncMethodRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeLockInAsyncMethod, SyntaxKind.LockStatement);
    }

    private static void AnalyzeLockInAsyncMethod(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var lockStatement = (LockStatementSyntax)ctx.Node;

        var enclosingMethod = AotSafetyHelpers.FindEnclosingMethodDeclaration(lockStatement);
        if (enclosingMethod is null) return;

        if (!enclosingMethod.Modifiers.Any(m => m.ValueText == "async")) return;

        var methodName = enclosingMethod.Identifier.ValueText;
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, lockStatement.GetLocation(), methodName));
    }
}
