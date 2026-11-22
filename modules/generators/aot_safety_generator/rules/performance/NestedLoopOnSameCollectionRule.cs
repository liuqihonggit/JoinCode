namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6001: 性能 — 嵌套循环遍历同一集合，潜在 O(n²) 复杂度。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6001",
    Title = "性能: 嵌套循环遍历同一集合，潜在 O(n²) 复杂度",
    Description = "嵌套循环在第 {0} 行遍历集合，外层循环在第 {1} 行也遍历集合。如果内层循环体依赖外层集合，复杂度为 O(n²)。考虑使用 HashSet/Dictionary 替代内层线性查找，或使用双指针/排序+二分查找优化。",
    Category = "PerformanceAudit",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "嵌套循环遍历同一集合是最常见的 O(n²) 来源. 优化方案优先级: 1) 编译期确定性优化(static readonly + 二分查找) 2) 空间换时间(HashSet/Dictionary 替代 List.Contains) 3) 对数复杂度算法(排序 + BinarySearch/LowerBound+UpperBound).")]
public sealed class NestedLoopOnSameCollectionRule : AnalyzerRuleBase<NestedLoopOnSameCollectionRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeNestedLoop,
            SyntaxKind.ForEachStatement, SyntaxKind.ForStatement,
            SyntaxKind.WhileStatement, SyntaxKind.DoStatement);
    }

    private static void AnalyzeNestedLoop(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var loopNode = ctx.Node;

        var depth = 0;
        var outerLoopLine = -1;
        var current = loopNode.Parent;
        while (current is not null) {
            if (current is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                or ForEachStatementSyntax or ForEachVariableStatementSyntax) {
                depth++;
                if (outerLoopLine < 0)
                    outerLoopLine = current.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
            current = current.Parent;
        }

        if (depth < 1) return;

        var innerLoopLine = loopNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        var innerCollection = GetLoopCollectionExpression(loopNode);
        if (innerCollection is null) return;

        var parent = loopNode.Parent;
        while (parent is not null) {
            if (parent is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                or ForEachStatementSyntax or ForEachVariableStatementSyntax) {
                var outerCollection = GetLoopCollectionExpression(parent);
                if (outerCollection is not null) {
                    var innerText = innerCollection.ToString().Replace(" ", "");
                    var outerText = outerCollection.ToString().Replace(" ", "");

                    if (innerText == outerText ||
                        innerText.StartsWith(outerText + ".", StringComparison.Ordinal) ||
                        innerText.Contains(outerText)) {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            Descriptor,
                            loopNode.GetLocation(),
                            innerLoopLine, outerLoopLine));
                        return;
                    }
                }
            }
            parent = parent.Parent;
        }
    }

    private static ExpressionSyntax? GetLoopCollectionExpression(SyntaxNode loopNode) {
        return loopNode switch {
            ForEachStatementSyntax foreachStmt => foreachStmt.Expression,
            ForEachVariableStatementSyntax foreachVarStmt => foreachVarStmt.Expression,
            ForStatementSyntax forStmt => null,
            _ => null
        };
    }
}
