namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3015: 异步返回值裸丢弃 — ExpressionStatement 级调用返回 Task/ValueTask 但未 await/未赋值/未 return。
/// 通用检测：基于 BCL Task-like 类型判断，不依赖项目特定方法名。
/// 排除：Dispose/DisposeAsync 方法体（由 JCC9200 管）、_ = 显式丢弃（由 JCC3001/3002 管）、lambda/本地函数内。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3015",
    Title = "异步红线: 异步方法返回值裸丢弃（fire-and-forget 未 await）",
    Description = "调用 '{0}' 返回 Task/ValueTask 但作为裸语句丢弃，未 await。异步操作未完成时后续代码已执行，导致竞态条件（如文件写入未完成即读取）。必须 await，或用 '_ = ' 显式标注 fire-and-forget 意图。",
    Category = "AsyncSafety",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "Root cause: bare invocation 'SomeAsyncMethod();' discards the returned Task. The async operation may not complete before the next statement runs, causing race conditions (e.g., file write incomplete when read). Fix: 1) add 'await' (make enclosing method async if needed); 2) or explicitly discard with '_ = SomeAsyncMethod();' to document fire-and-forget intent (JCC3001/3002 will then check CancellationToken).")]
public sealed class AsyncReturnDiscardedRule : AnalyzerRuleBase<AsyncReturnDiscardedRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ExpressionStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var exprStatement = (ExpressionStatementSyntax)ctx.Node;

        if (exprStatement.Expression is not InvocationExpressionSyntax invocation) return;

        if (AotSafetyHelpers.IsInsideAwait(exprStatement)) return;

        if (IsDiscardAssignment(exprStatement)) return;

        var enclosingMethod = exprStatement.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (enclosingMethod is not null) {
            var methodSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingMethod);
            if (methodSymbol is not null && AotSafetyHelpers.IsDisposeMethod(methodSymbol)) return;

            if (enclosingMethod.Body is not null &&
                AotSafetyHelpers.IsInsideLambdaOrLocalFunction(exprStatement, enclosingMethod.Body)) return;

            if (!enclosingMethod.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) return;
        }

        var methodSymbol2 = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (!AotSafetyHelpers.ReturnsTaskLike(methodSymbol2)) return;

        var displayName = GetInvocationDisplayName(invocation, methodSymbol2);
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation(), displayName));
    }

    private static bool IsDiscardAssignment(ExpressionStatementSyntax stmt) {
        if (stmt.Expression is not AssignmentExpressionSyntax assign) return false;
        return assign.Left is IdentifierNameSyntax { Identifier.ValueText: "_" };
    }

    private static string GetInvocationDisplayName(InvocationExpressionSyntax invocation, IMethodSymbol? symbol) {
        if (symbol is null) return invocation.Expression.ToString();
        var typeName = symbol.ContainingType?.Name ?? "?";
        return $"{typeName}.{symbol.Name}";
    }
}
