namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6007: 性能 — 字符串操作可优化为 Span&lt;char&gt; 减少分配。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6007",
    Title = "性能: 字符串操作 '{0}' 可优化为 Span<char> 减少分配",
    Description = "在热路径中对 string 调用 {0} 会产生中间字符串分配。使用 ReadOnlySpan<char> 或 AsSpan() 可避免分配，提升性能。",
    Category = "PerformanceAudit",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "string.Substring/Remove/Split 等操作会创建新字符串对象. Span<char> 是栈上结构体，不分配堆内存. AsSpan() 是零拷贝切片. 在高频调用路径中差异显著.")]
public sealed class StringToSpanRule : AnalyzerRuleBase<StringToSpanRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeStringToSpan, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeStringToSpan(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (methodName != "Substring" && methodName != "Remove" && methodName != "Split") return;

        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType;
        if (containingType is null) return;
        if (containingType.SpecialType != SpecialType.System_String) return;

        if (!AotSafetyHelpers.IsInsideLoop(invocation)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Descriptor,
            invocation.GetLocation(),
            methodName));
    }
}
