namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6005: 性能 — List&lt;T&gt;.Insert(0, item) 头部插入是 O(n) 操作。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6005",
    Title = "性能: List<T>.Insert(0, item) 头部插入是 O(n) 操作",
    Description = "List<T>.Insert(0, item) 需要移动所有现有元素，复杂度 O(n)。如果频繁头部插入，建议: 1) 从尾部 Add 后 Reverse; 2) 使用 Stack<T> (LIFO); 3) 使用 LinkedList<T> (但随机访问 O(n))。",
    Category = "PerformanceAudit",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "List<T> 底层是数组，Insert(0, item) 需要 Array.Copy 移动所有元素. 在循环内 Insert(0,..) 是 O(n²). 替代方案: Add + Reverse 是 O(n); Stack<T>.Push 是 O(1); LinkedList<T>.AddFirst 是 O(1) 但失去索引访问.")]
public sealed class ListInsertAtHeadRule : AnalyzerRuleBase<ListInsertAtHeadRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeListInsertAtHead, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeListInsertAtHead(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (memberAccess.Name.Identifier.ValueText != "Insert") return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2) return;

        var firstArg = args[0].Expression;
        var constantValue = ctx.SemanticModel.GetConstantValue(firstArg);
        if (!constantValue.HasValue) return;
        if (constantValue.Value is not int index || index != 0) return;

        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType;
        if (containingType is null) return;
        if (!IsListOrArrayType(containingType)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation()));
    }

    private static bool IsListOrArrayType(INamedTypeSymbol type) {
        if (type.TypeKind == TypeKind.Array) return true;

        if (!type.IsGenericType) return false;
        var def = type.ConstructedFrom;
        if (def is null) return false;
        var fullName = $"{def.ContainingNamespace?.ToDisplayString()}.{def.Name}";
        return fullName == "System.Collections.Generic.List";
    }
}
