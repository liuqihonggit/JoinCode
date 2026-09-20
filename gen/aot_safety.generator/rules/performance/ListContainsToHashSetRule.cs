namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6003: 性能 — List&lt;T&gt; 在方法内调用 Contains 共 N 次，建议替换为 HashSet&lt;T&gt;。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6003",
    Title = "性能: List<T> 在方法内调用 Contains 共 {0} 次，建议替换为 HashSet<T>",
    Description = "变量 '{1}' (List<{2}>) 在此方法内调用 Contains 共 {0} 次。List.Contains 是 O(n)，HashSet.Contains 是 O(1)。如果元素不需要重复且顺序不重要，建议将类型改为 HashSet<{2}>。",
    Category = "PerformanceAudit",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "频繁调用 List.Contains 是典型的空间换时间优化场景. HashSet<T> 的 Contains 是 O(1)，但会失去索引访问和元素顺序. 如果需要保留顺序，可用 HashSet 查找 + List 遍历的双数据结构模式.")]
public sealed class ListContainsToHashSetRule : AnalyzerRuleBase<ListContainsToHashSetRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeMethodLevelListContains, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethodLevelListContains(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var method = (MethodDeclarationSyntax)ctx.Node;

        var containsCounts = new Dictionary<string, List<InvocationExpressionSyntax>>(StringComparer.Ordinal);
        var variableTypes = new Dictionary<string, (string TypeName, string ElementType)>(StringComparer.Ordinal);

        foreach (var descendant in method.DescendantNodes()) {
            if (descendant is not InvocationExpressionSyntax invocation) continue;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;
            if (memberAccess.Name.Identifier.ValueText != "Contains") continue;

            var receiverText = memberAccess.Expression?.ToString() ?? "";
            if (string.IsNullOrEmpty(receiverText)) continue;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) continue;

            var containingType = symbol.ContainingType;
            if (containingType is null || !IsListOrArrayType(containingType)) continue;

            var elementType = "T";
            if (containingType.TypeArguments.Length > 0)
                elementType = containingType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            if (!containsCounts.ContainsKey(receiverText)) {
                containsCounts[receiverText] = new List<InvocationExpressionSyntax>();
                variableTypes[receiverText] = ("List", elementType);
            }
            containsCounts[receiverText].Add(invocation);
        }

        const int threshold = 3;
        foreach (var kvp in containsCounts) {
            if (kvp.Value.Count < threshold) continue;

            var varName = kvp.Key;
            var (_, elementType) = variableTypes[varName];
            var firstInvocation = kvp.Value[0];

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                firstInvocation.GetLocation(),
                kvp.Value.Count, varName, elementType));
        }
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
