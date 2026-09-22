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
        var invocation = exprStatement.Expression as InvocationExpressionSyntax;
        if (invocation is null) {
            if (exprStatement.Expression is AssignmentExpressionSyntax assign) {
                invocation = assign.Right as InvocationExpressionSyntax;
            }
        }
        if (invocation is null) return;

        if (IsConsumed(exprStatement)) return;

        var enclosingMethod = exprStatement.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (enclosingMethod is null) return;

        if (enclosingMethod.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) return;

        var methodSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingMethod);
        if (methodSymbol is null) return;

        if (enclosingMethod.Body is not null &&
            AotSafetyHelpers.IsInsideLambdaOrLocalFunction(exprStatement, enclosingMethod.Body)) return;

        var calledSymbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (!AotSafetyHelpers.ReturnsTaskLike(calledSymbol)) return;

        var enclosingName = methodSymbol.Name;
        var calledName = calledSymbol!.ContainingType?.Name is { } tn ? $"{tn}.{calledSymbol.Name}" : calledSymbol.Name;
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation(), enclosingName, calledName));
    }

    private static bool IsConsumed(ExpressionStatementSyntax stmt) {
        if (AotSafetyHelpers.IsInsideAwait(stmt)) return true;

        var expr = stmt.Expression;
        if (expr is AssignmentExpressionSyntax assign) {
            var right = assign.Right;
            if (IsBlockingConsumption(right)) return true;
        }
        return false;
    }

    private static bool IsBlockingConsumption(ExpressionSyntax expr) {
        if (expr is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is MemberAccessExpressionSyntax ma) {
            var name = ma.Name.Identifier.ValueText;
            if (name is "GetResult" or "Wait") return true;
        }
        return false;
    }
}
