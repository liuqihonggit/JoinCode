namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3018: 释放函数内 fire-and-forget — 语义检测 IDisposable/IAsyncDisposable 实现方法体内异步调用未 await。
/// 与 JCC9200 互补：JCC9200 靠方法名 Dispose/DisposeAsync，JCC3018 靠语义检测接口实现（覆盖显式接口实现、override 链）。
/// 通用检测：基于 BCL 接口契约（IDisposable/IAsyncDisposable），不依赖项目特定方法名。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3018",
    Title = "异步红线: 释放函数内 fire-and-forget 异步调用",
    Description = "释放方法 '{0}'（实现 IDisposable/IAsyncDisposable）内调用 '{1}' 返回 Task/ValueTask 但未 await。释放路径必须等待异步清理完成，否则掩盖真实错误。必须改为 await 调用。",
    Category = "AsyncSafety",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "A dispose method (IDisposable.Dispose / IAsyncDisposable.DisposeAsync, detected via interface implementation not method name) contains a fire-and-forget async call. The dispose returns before async cleanup completes, masking errors. Fix: add await (make DisposeAsync if needed). This rule complements JCC9200 which detects by method name; JCC3018 detects by interface implementation semantics.")]
public sealed class DisposeMethodFireAndForgetRule : AnalyzerRuleBase<DisposeMethodFireAndForgetRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;
        var methodSymbol = ctx.SemanticModel.GetDeclaredSymbol(methodDecl);
        if (methodSymbol is null) return;

        if (methodSymbol.Name is "Dispose" or "DisposeAsync") return;

        if (!AotSafetyHelpers.IsDisposeInterfaceImplementation(methodSymbol)) return;

        var body = methodDecl.Body;
        if (body is null) return;

        foreach (var stmt in body.DescendantNodes().OfType<ExpressionStatementSyntax>()) {
            if (AotSafetyHelpers.IsInsideLambdaOrLocalFunction(stmt, body)) continue;
            if (AotSafetyHelpers.IsInsideAwait(stmt)) continue;

            var expr = stmt.Expression;
            InvocationExpressionSyntax? invocation = null;

            if (expr is AssignmentExpressionSyntax assign && assign.Left is IdentifierNameSyntax { Identifier.ValueText: "_" }) {
                invocation = assign.Right as InvocationExpressionSyntax;
            } else if (expr is InvocationExpressionSyntax inv) {
                invocation = inv;
            }

            if (invocation is null) continue;

            var calledSymbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (!AotSafetyHelpers.ReturnsTaskLike(calledSymbol)) continue;

            var calledName = calledSymbol!.ContainingType?.Name is { } tn ? $"{tn}.{calledSymbol.Name}" : calledSymbol.Name;
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation(), methodSymbol.Name, calledName));
        }
    }
}
