namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9106: 资源释放 — Dispose 方法内 try-catch 样板应改用 DisposeSafe 扩展方法。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9106",
    Title = "资源释放: Dispose 方法内 try-catch 样板应改用 DisposeSafe 扩展方法",
    Description = "Dispose/DisposeAsync 方法体内第 {0} 个 try-catch 捕获 {1}，是典型样板代码。应改用 DisposeSafe/DisposeSafeAsync/CancelAndDisposeSafe 扩展方法（JoinCode.Abstractions.Utils.DisposeSafeExtensions），消除 try-catch 样板。",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "AGENTS.md 规则3: Dispose 方法内禁止写 try { x.Dispose(); } catch (ObjectDisposedException) 样板，统一调 x.DisposeSafe(_logger)。" +
    "正确做法: 1) 'try { x.Dispose(); } catch (ObjectDisposedException) { ... }' → 'x.DisposeSafe(_logger)'; " +
    "2) 'try { await x.DisposeAsync(); } catch (ObjectDisposedException) { ... }' → 'await x.DisposeSafeAsync(_logger)'; " +
    "3) 'try { cts.Cancel(); } catch ...; try { cts.Dispose(); } catch ...' → 'cts.CancelAndDisposeSafe(_logger)'。" +
    "DisposeSafe 已吞 ObjectDisposedException（幂等），其他异常可选日志.")]
public sealed class DisposeTryCatchRule : AnalyzerRuleBase<DisposeTryCatchRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeDisposeMethodTryCatch, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeDisposeMethodTryCatch(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;
        var methodName = methodDecl.Identifier.ValueText;
        if (methodName is not "Dispose" and not "DisposeAsync") return;

        if (methodDecl.Body is null) return;

        var tryStatements = methodDecl.Body
            .DescendantNodes()
            .OfType<TryStatementSyntax>()
            .Where(t => t.Catches.Count > 0)
            .ToList();
        if (tryStatements.Count == 0) return;

        foreach (var tryStmt in tryStatements) {
            var catchInfo = AnalyzeCatchClauses(tryStmt.Catches);
            if (catchInfo is null) continue;

            if (!TryBlockIsSimpleDisposeOrCancel(tryStmt.Block)) continue;

            if (!IsSimpleCatchBlock(tryStmt.Catches)) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                tryStmt.TryKeyword.GetLocation(),
                catchInfo.Value.index,
                catchInfo.Value.exceptionName));
        }
    }

    private static bool IsSimpleCatchBlock(SyntaxList<CatchClauseSyntax> catches) {
        foreach (var catchClause in catches) {
            if (catchClause.Block is null) continue;
            var statements = catchClause.Block.Statements;
            if (statements.Count == 0) continue;
            foreach (var stmt in statements) {
                if (stmt is not ExpressionStatementSyntax exprStmt) return false;
                if (exprStmt.Expression is not InvocationExpressionSyntax inv) return false;
                var name = GetMemberName(inv);
                if (name.StartsWith("Log", StringComparison.Ordinal) ||
                    name is "WriteLine" or "Write" or "WriteLineAsync" or "WriteAsync") continue;
                return false;
            }
        }
        return true;
    }

    private static string GetMemberName(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Name.Identifier.ValueText;
        if (invocation.Expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.ValueText;
        return string.Empty;
    }

    private static (int index, string exceptionName)? AnalyzeCatchClauses(SyntaxList<CatchClauseSyntax> catches) {
        for (var i = 0; i < catches.Count; i++) {
            var catchClause = catches[i];
            if (catchClause.Declaration is null) continue;
            var typeName = catchClause.Declaration.Type.ToString();
            if (typeName.Contains("ObjectDisposedException"))
                return (i + 1, typeName);
            if (typeName.Contains("Exception"))
                return (i + 1, typeName);
        }
        return null;
    }

    private static bool TryBlockIsSimpleDisposeOrCancel(BlockSyntax? block) {
        if (block is null) return false;
        if (block.Statements.Count == 0) return false;
        foreach (var stmt in block.Statements) {
            if (stmt is not ExpressionStatementSyntax exprStmt) return false;
            var expr = exprStmt.Expression;
            if (expr is AwaitExpressionSyntax awaitExpr)
                expr = awaitExpr.Expression;
            if (expr is not InvocationExpressionSyntax inv) return false;
            var name = GetMemberName(inv);
            if (name is not "Dispose" and not "DisposeAsync" and not "Cancel" and not "CancelAsync")
                return false;
        }
        return true;
    }
}
