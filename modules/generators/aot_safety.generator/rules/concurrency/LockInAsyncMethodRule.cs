namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC4001: 死锁风险 — lock 语句在 async 方法中使用。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC4001",
    Title = "死锁风险: lock 语句在 async 方法中使用",
    Description = "lock 语句在 async 方法 '{0}' 中使用. lock 块内不能 await；若持锁路径间接 await 需要同一锁的操作会死锁. 本项目禁止显式锁：改用 Actor 单写者邮箱（Channel<T>）、不可变快照 + CAS 或单飞 TaskCompletionSource.",
    Category = "DeadlockSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "零锁并发红线（JCC4008）: lock/Monitor 在异步路径中会因线程切换与重入产生死锁。正确做法: 1) 把状态变更收口到 Actor 单写者邮箱; 2) 只读快照用 Interlocked.CompareExchange 发布; 3) 单飞用 TaskCompletionSource + CAS. 禁止用 SemaphoreSlim(1,1) 作为替代锁.")]
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
