namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6006: 性能 — 循环中的范围查询可使用二分查找优化为 O(log n)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6006",
    Title = "性能: 循环中的范围查询可使用二分查找优化为 O(log n)",
    Description = "循环中对已排序集合执行范围条件判断（'{0}'）是 O(n) 扫描。如果集合已排序，使用 Array.BinarySearch 或自定义 LowerBound/UpperBound 可将范围查询优化为 O(log n)。",
    Category = "PerformanceAudit",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "已排序集合的范围查询（如 arr[i] >= low && arr[i] <= high）不需要遍历全部元素. 二分查找定位下界和上界即可确定范围. Array.BinarySearch 是 O(log n). 自定义 LowerBound/UpperBound 也是 O(log n).")]
public sealed class RangeQueryBinarySearchRule : AnalyzerRuleBase<RangeQueryBinarySearchRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeRangeQueryBinarySearch, SyntaxKind.ForEachStatement, SyntaxKind.ForStatement);
    }

    private static void AnalyzeRangeQueryBinarySearch(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var loopBody = ctx.Node switch {
            ForEachStatementSyntax fe => fe.Statement as BlockSyntax,
            ForStatementSyntax f => f.Statement as BlockSyntax,
            _ => null,
        };
        if (loopBody is null) return;

        foreach (var statement in loopBody.Statements) {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            if (statement is not IfStatementSyntax ifStmt) continue;
            if (!IsRangeCondition(ifStmt.Condition)) continue;

            var conditionText = ifStmt.Condition.ToString();
            if (conditionText.Length > 60)
                conditionText = conditionText.Substring(0, 57) + "...";

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                ifStmt.Condition.GetLocation(),
                conditionText));
        }
    }

    private static bool IsRangeCondition(ExpressionSyntax? condition) {
        if (condition is null) return false;

        if (condition is not BinaryExpressionSyntax binary) return false;
        if (!binary.IsKind(SyntaxKind.LogicalAndExpression)) return false;

        var left = binary.Left;
        var right = binary.Right;

        return IsComparisonWithVariable(left) && IsComparisonWithVariable(right);
    }

    private static bool IsComparisonWithVariable(ExpressionSyntax expr) {
        return expr.Kind() switch {
            SyntaxKind.GreaterThanOrEqualExpression => true,
            SyntaxKind.LessThanOrEqualExpression => true,
            SyntaxKind.GreaterThanExpression => true,
            SyntaxKind.LessThanExpression => true,
            _ => false,
        };
    }
}
