namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC5002: 性能 — 循环内字符串拼接使用 += 运算符。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC5002",
    Title = "性能: 循环内字符串拼接使用 += 运算符",
    Description = "在循环内使用 += 拼接字符串会导致大量中间字符串分配。应使用 StringBuilder 或 string.Concat 替代。",
    Category = "PerformanceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Strings are immutable; each += creates a new string object. Accumulating concatenation in a loop causes O(n^2) memory allocation. StringBuilder is mutable with O(1) append and O(n) final ToString.")]
public sealed class StringConcatInLoopRule : AnalyzerRuleBase<StringConcatInLoopRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeStringConcatInLoop, SyntaxKind.AddAssignmentExpression);
    }

    private static void AnalyzeStringConcatInLoop(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var addAssignment = (AssignmentExpressionSyntax)ctx.Node;

        var leftType = ctx.SemanticModel.GetTypeInfo(addAssignment.Left).Type;
        if (leftType is null) return;
        if (leftType.SpecialType != SpecialType.System_String) return;

        if (!AotSafetyHelpers.IsInsideLoop(addAssignment)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, addAssignment.GetLocation()));
    }
}
