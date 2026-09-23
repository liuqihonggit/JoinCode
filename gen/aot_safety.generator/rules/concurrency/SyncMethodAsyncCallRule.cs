namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3016: 同步方法内异步调用未消费 — 非 async 方法体内调用返回 Task 的方法，但未通过 await/GetAwaiter().GetResult()/.Wait()/.Result 消费。
/// 通用检测：基于 BCL Task-like 类型判断，不依赖项目特定方法名。
/// 修复方向：改 async + await，或用 .GetAwaiter().GetResult() 显式同步阻塞（需注明理由）。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3016",
    Title = "异步红线: 同步方法内异步调用未消费",
    Description = "同步方法 '{0}' 内调用 '{1}' 返回 Task/ValueTask 但未消费（未 await/GetAwaiter().GetResult()/.Wait()/.Result）。异步操作结果被丢弃。修复：改 async + await，或用 .GetAwaiter().GetResult() 显式同步阻塞（需注明理由）。",
    Category = "AsyncSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "A non-async method calls a Task-returning method but does not consume the result. The async operation may not complete. Fix: 1) make enclosing method async and add await; 2) or explicitly block with .GetAwaiter().GetResult() (document why blocking is safe — no SynchronizationContext); 3) or use a synchronous overload if available.")]
public sealed class SyncMethodAsyncCallRule : AnalyzerRuleBase<SyncMethodAsyncCallRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ExpressionStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var exprStatement = (ExpressionStatementSyntax)ctx.Node;

        // 检测逻辑委托给共享检测器 — 分析器和 ast_cli 调用同一套逻辑
        if (!SyncMethodAsyncCallDetector.IsViolation(exprStatement, ctx.SemanticModel, ctx.CancellationToken)) return;

        var invocation = SyncMethodAsyncCallDetector.GetInvocation(exprStatement);
        var info = SyncMethodAsyncCallDetector.GetViolationInfo(exprStatement, ctx.SemanticModel);
        if (invocation is null || info is null) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation(), info.Value.enclosingName, info.Value.calledName));
    }
}
