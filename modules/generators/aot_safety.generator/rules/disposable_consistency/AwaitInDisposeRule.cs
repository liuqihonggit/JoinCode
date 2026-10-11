namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9200: Dispose 释放完整性 — Dispose/DisposeAsync 方法体内禁止 fire-and-forget 异步调用。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9200",
    Title = "Dispose 释放完整性: Dispose/DisposeAsync 方法体内禁止 fire-and-forget 异步调用",
    Description = "Dispose/DisposeAsync 方法体内 fire-and-forget 调用 '{0}'(第 {1} 行) — 释放路径射后不理会掩盖真实错误(异步清理未完成即返回)。必须改为 await 调用,确保释放完整完成。",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "Root cause: fire-and-forget (_ = xxxAsync() or bare xxxAsync() without await) makes Dispose return before async cleanup completes, masking real errors." +
    "Fix: change '_ = xxxAsync()' to 'await xxxAsync().ConfigureAwait(false)'; in sync Dispose, remove the line (cannot await)." +
    "See CronSchedulerService.DisposeAsync and ActorBase.DisposeAsync for past deadlock incidents.")]
public sealed class AwaitInDisposeRule : AnalyzerRuleBase<AwaitInDisposeRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeDisposeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeDisposeMethod(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;
        var methodName = methodDecl.Identifier.ValueText;
        if (methodName is not ("Dispose" or "DisposeAsync")) return;

        var body = methodDecl.Body;
        if (body is null) return;

        foreach (var stmt in body.DescendantNodes().OfType<ExpressionStatementSyntax>()) {
            if (IsInsideLambdaOrLocalFunction(stmt, body)) continue;

            var expr = stmt.Expression;
            string? fireAndForgetMethodName = null;

            if (expr is AssignmentExpressionSyntax assign && assign.Left is IdentifierNameSyntax { Identifier.ValueText: "_" }) {
                var invoked = assign.Right;
                if (invoked is InvocationExpressionSyntax inv)
                    fireAndForgetMethodName = GetInvocationName(inv);
            } else if (expr is InvocationExpressionSyntax inv) {
                fireAndForgetMethodName = GetInvocationName(inv);
            }

            if (fireAndForgetMethodName is null) continue;

            var typeInfo = ctx.SemanticModel.GetTypeInfo(expr);
            if (IsTaskType(typeInfo.Type)) {
                var line = stmt.SyntaxTree.GetLineSpan(stmt.Span).StartLinePosition.Line + 1;
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptor,
                    stmt.Expression.GetLocation(),
                    fireAndForgetMethodName,
                    line));
            }
        }
    }

    private static string? GetInvocationName(InvocationExpressionSyntax inv) {
        return inv.Expression switch {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null
        };
    }

    private static bool IsTaskType(ITypeSymbol? type) {
        if (type is null) return false;
        var name = type.OriginalDefinition.ToDisplayString();
        return name is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask"
            or "System.Threading.Tasks.Task<T>" or "System.Threading.Tasks.ValueTask<T>";
    }

    private static bool IsInsideLambdaOrLocalFunction(SyntaxNode node, BlockSyntax methodBody) {
        var current = node.Parent;
        while (current is not null && current != methodBody) {
            if (current is SimpleLambdaExpressionSyntax or
                ParenthesizedLambdaExpressionSyntax or
                LocalFunctionStatementSyntax)
                return true;
            current = current.Parent;
        }
        return false;
    }
}
