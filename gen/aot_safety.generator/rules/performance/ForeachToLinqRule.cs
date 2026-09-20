namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6008: 代码风格 — foreach 循环可替换为 LINQ 链式表达式。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6008",
    Title = "代码风格: foreach 循环可替换为 LINQ 链式表达式",
    Description = "foreach 循环体仅包含 {0} 操作，可替换为更简洁的 LINQ 链式表达式: {1}。声明式代码更易读、更易维护。",
    Category = "PerformanceAudit",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "LINQ 链式编程是 C# 的核心范式. foreach + if + Add → .Where().ToList(); foreach + break → .FirstOrDefault(); foreach + return true → .Any(); foreach + sum += → .Sum(). 声明式代码意图更清晰，减少样板代码和 off-by-one 错误.")]
public sealed class ForeachToLinqRule : AnalyzerRuleBase<ForeachToLinqRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeForeachToLinq, SyntaxKind.ForEachStatement);
    }

    private static void AnalyzeForeachToLinq(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        if (ctx.Node is not ForEachStatementSyntax foreachStmt) return;
        var body = foreachStmt.Statement as BlockSyntax;
        if (body is null) return;

        if (body.Statements.Count > 3) return;

        if (TryMatchFilterAndAdd(body, out var filterAddDesc)) {
            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                foreachStmt.ForEachKeyword.GetLocation(),
                "过滤+收集", filterAddDesc));
            return;
        }

        if (TryMatchAnyPattern(body, out var anyDesc)) {
            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                foreachStmt.ForEachKeyword.GetLocation(),
                "存在判断", anyDesc));
            return;
        }

        if (TryMatchAggregation(body, out var aggDesc)) {
            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                foreachStmt.ForEachKeyword.GetLocation(),
                "聚合", aggDesc));
            return;
        }
    }

    private static bool TryMatchFilterAndAdd(BlockSyntax body, out string suggestion) {
        suggestion = "";
        if (body.Statements.Count != 1) return false;

        if (body.Statements[0] is not IfStatementSyntax ifStmt) return false;

        if (ContainsLinearSearch(ifStmt.Condition)) return false;

        var thenStatement = GetSingleStatement(ifStmt.Statement);
        if (thenStatement is null) return false;

        if (thenStatement is not ExpressionStatementSyntax exprStmt) return false;
        if (exprStmt.Expression is not InvocationExpressionSyntax invocation) return false;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return false;

        if (memberAccess.Name.Identifier.ValueText == "Add") {
            suggestion = ".Where(...).ToList()";
            return true;
        }

        return false;
    }

    private static bool TryMatchAnyPattern(BlockSyntax body, out string suggestion) {
        suggestion = "";
        if (body.Statements.Count != 1) return false;

        if (body.Statements[0] is not IfStatementSyntax ifStmt) return false;

        if (ContainsLinearSearch(ifStmt.Condition)) return false;

        var thenStatement = GetSingleStatement(ifStmt.Statement);
        if (thenStatement is null) return false;

        if (thenStatement is not ReturnStatementSyntax returnStmt) return false;
        if (returnStmt.Expression is null) return false;

        var returnText = returnStmt.Expression.ToString().Trim();
        if (returnText == "true") {
            suggestion = ".Any(...)";
            return true;
        }

        if (returnText == "false") {
            suggestion = ".All(...)";
            return true;
        }

        return false;
    }

    private static bool ContainsLinearSearch(ExpressionSyntax? expr) {
        if (expr is null) return false;
        foreach (var node in expr.DescendantNodesAndSelf()) {
            if (node is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess) {
                var name = memberAccess.Name.Identifier.ValueText;
                if (name == "Contains" || name == "IndexOf")
                    return true;
            }
        }
        return false;
    }

    private static StatementSyntax? GetSingleStatement(StatementSyntax thenStatement) {
        if (thenStatement is BlockSyntax block) {
            if (block.Statements.Count != 1) return null;
            return block.Statements[0];
        }
        return thenStatement;
    }

    private static bool TryMatchAggregation(BlockSyntax body, out string suggestion) {
        suggestion = "";
        if (body.Statements.Count != 1) return false;

        if (body.Statements[0] is not ExpressionStatementSyntax exprStmt) return false;

        if (exprStmt.Expression.IsKind(SyntaxKind.AddAssignmentExpression)) {
            suggestion = ".Sum(...)";
            return true;
        }

        if (exprStmt.Expression.IsKind(SyntaxKind.PostIncrementExpression) ||
            exprStmt.Expression.IsKind(SyntaxKind.PreIncrementExpression)) {
            suggestion = ".Count(...)";
            return true;
        }

        return false;
    }
}
