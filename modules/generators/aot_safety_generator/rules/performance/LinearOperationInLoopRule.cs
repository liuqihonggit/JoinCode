namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6002: 性能 — 循环内调用 O(n) 操作，整体复杂度为 O(n²)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6002",
    Title = "性能: 循环内调用 O(n) 操作 '{0}'，整体复杂度为 O(n²)",
    Description = "在循环内调用 '{0}'（O(n) 操作），整体复杂度为 O(n²)。建议: Contains/IndexOf/Find → 用 HashSet<T> 替代(查找 O(1)); RemoveAt → 从尾部删除或用 Queue/Stack; Insert(0,..) → 用 LinkedList 或从尾部添加后 Reverse。",
    Category = "PerformanceAudit",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "循环内线性操作是最常见的 O(n²) 模式. List<T>.Contains/IndexOf/Find 是 O(n)，在循环内调用导致 O(n²). HashSet<T>.Contains 是 O(1)，Dictionary<TKey,TValue> 查找也是 O(1).")]
public sealed class LinearOperationInLoopRule : AnalyzerRuleBase<LinearOperationInLoopRule> {
    private static readonly HashSet<string> LinearOperationMethods = new(StringComparer.Ordinal)
    {
        "Contains", "IndexOf", "Find", "FindIndex", "FindLast", "FindLastIndex",
        "RemoveAt", "Reverse",
    };

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeLinearOperationInLoop, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeLinearOperationInLoop(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (!AotSafetyHelpers.IsInsideLoop(invocation)) return;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (!LinearOperationMethods.Contains(methodName)) return;

        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType;
        if (containingType is null) return;

        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (!IsListOrArrayType(containingType)) return;

        if (methodName == "RemoveAt" && IsTailRemoval(invocation, ctx)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Descriptor,
            invocation.GetLocation(),
            $"{typeName}.{methodName}"));
    }

    private static bool IsTailRemoval(InvocationExpressionSyntax invocation, SyntaxNodeAnalysisContext ctx) {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0) return false;

        var firstArg = args[0].Expression;

        var constantValue = ctx.SemanticModel.GetConstantValue(firstArg);
        if (constantValue.HasValue) return false;

        if (IsCountMinusOne(firstArg)) return true;

        if (firstArg is PrefixUnaryExpressionSyntax prefixUnary &&
            prefixUnary.IsKind(SyntaxKind.IndexExpression)) {
            var operandText = prefixUnary.Operand.ToString().Trim();
            if (operandText == "1") return true;
        }

        if (firstArg is IdentifierNameSyntax identifier) {
            var varName = identifier.Identifier.ValueText;
            if (IsVariableAssignedAsCountMinusOne(identifier, varName, ctx)) return true;
        }

        return false;
    }

    private static bool IsCountMinusOne(ExpressionSyntax expr) {
        if (expr is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.SubtractExpression) &&
            binary.Right is LiteralExpressionSyntax literal &&
            literal.Token.ValueText == "1") {
            var leftText = binary.Left.ToString().Replace(" ", "");
            if (leftText.EndsWith(".Count", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool IsVariableAssignedAsCountMinusOne(IdentifierNameSyntax identifier, string varName, SyntaxNodeAnalysisContext ctx) {
        var statement = identifier.Parent;
        while (statement is not null && statement is not StatementSyntax)
            statement = statement.Parent;
        if (statement is null) return false;

        var block = statement.Parent;
        if (block is not BlockSyntax and not SwitchSectionSyntax) return false;

        var removeAtSpanStart = statement.SpanStart;

        foreach (var child in block.ChildNodes()) {
            if (child.SpanStart >= removeAtSpanStart) break;

            if (child is LocalDeclarationStatementSyntax localDecl) {
                foreach (var v in localDecl.Declaration.Variables) {
                    if (v.Identifier.ValueText != varName) continue;
                    if (v.Initializer?.Value is not null && IsCountMinusOne(v.Initializer.Value))
                        return true;
                }
            }

            if (child is ExpressionStatementSyntax exprStmt &&
                exprStmt.Expression is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax assignTarget &&
                assignTarget.Identifier.ValueText == varName) {
                if (IsCountMinusOne(assignment.Right)) return true;
            }
        }

        return false;
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
