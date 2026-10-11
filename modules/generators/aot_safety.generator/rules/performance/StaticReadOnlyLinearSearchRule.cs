namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6004: 性能 — static readonly 集合上的线性查找，建议使用二分查找。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6004",
    Title = "性能: static readonly 集合 '{0}' 上的线性查找，建议使用二分查找",
    Description = "字段 '{0}' 是 static readonly，数据在编译期确定且运行时不变。对其调用 {1} 是 O(n) 线性查找。如果集合已排序，可使用 Array.BinarySearch (O(log n)) 替代；如果需要范围查询，使用 LowerBound/UpperBound 双二分查找。",
    Category = "PerformanceAudit",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "编译期确定性优化是最优方案. static readonly 数据不会在运行时改变，排序一次后可用二分查找. Array.BinarySearch 是 O(log n)，比 Contains/IndexOf 的 O(n) 快数十倍. 范围查询用 LowerBound+UpperBound 也是 O(log n).")]
public sealed class StaticReadOnlyLinearSearchRule : AnalyzerRuleBase<StaticReadOnlyLinearSearchRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeStaticReadOnlyLinearSearch, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeStaticReadOnlyLinearSearch(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (methodName != "Contains" && methodName != "IndexOf") return;

        var receiverText = memberAccess.Expression?.ToString() ?? "";
        if (string.IsNullOrEmpty(receiverText)) return;

        var receiverExpr = memberAccess.Expression;
        if (receiverExpr is null) return;
        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(receiverExpr);
        if (symbolInfo.Symbol is not IFieldSymbol fieldSymbol) return;

        if (!fieldSymbol.IsStatic || !fieldSymbol.IsReadOnly) return;

        var fieldType = fieldSymbol.Type;
        if (!IsArrayType(fieldType) && !IsListType(fieldType)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Descriptor,
            invocation.GetLocation(),
            receiverText, methodName));
    }

    private static bool IsArrayType(ITypeSymbol type) {
        return type.TypeKind == TypeKind.Array;
    }

    private static bool IsListType(ITypeSymbol type) {
        if (type is not INamedTypeSymbol namedType) return false;
        if (!namedType.IsGenericType) return false;
        var def = namedType.ConstructedFrom;
        if (def is null) return false;
        var fullName = $"{def.ContainingNamespace?.ToDisplayString()}.{def.Name}";
        return fullName == "System.Collections.Generic.List";
    }
}
