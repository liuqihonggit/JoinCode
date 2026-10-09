namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC11005: 可空性 — 公开 API 返回 T? 且有 return null 时, 建议改用 TryGet 模式。
/// 粗筛检测: public 方法返回引用类型 T? (非集合, 集合由 JCC11004 覆盖), 方法名不以 Try 开头, 无 out 参数, 方法体有 return null。
/// TryGet 模式: bool TryGetXxx(out T result) — 调用方强制处理"找不到"情况, 比 return null 更安全。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "NullableContainer",
    Id = "JCC11005",
    Title = "可空性: 公开 API 返回 T? 建议改用 TryGet 模式",
    Description = "公开方法 '{0}' 返回可空引用类型且有 return null。建议改用 TryGet 模式: bool TryGet{0}(out T result)。TryGet 强制调用方处理'找不到'情况, 比 return null 更安全(调用方不会忘记 null 检查)。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Public APIs returning T? with return null should consider the TryGet pattern: bool TryGetXxx(out T result). The TryGet pattern forces callers to handle the 'not found' case, unlike return null which callers can silently ignore leading to NullReferenceException. This is a heuristic: fires on public methods returning nullable reference types (non-collection) with return null, whose name doesn't start with 'Try' and have no out parameters. Suppress with #pragma if the nullable return is intentional.")]
public sealed class TryGetPatternRule : AnalyzerRuleBase<TryGetPatternRule> {
    private static readonly HashSet<string> ContainerTypeNames = new(StringComparer.Ordinal)
    {
        "List", "Dictionary", "HashSet", "SortedList", "SortedDictionary", "SortedSet",
        "Stack", "Queue", "LinkedList", "Collection",
        "ReadOnlyCollection", "ReadOnlyDictionary", "ReadOnlyList",
        "ImmutableList", "ImmutableArray", "ImmutableHashSet", "ImmutableDictionary",
        "ImmutableSortedSet", "ImmutableSortedDictionary", "ImmutableQueue", "ImmutableStack",
        "FrozenSet", "FrozenDictionary", "FrozenList",
        "ConcurrentBag", "ConcurrentDictionary", "ConcurrentQueue", "ConcurrentStack",
        "IEnumerable", "IEnumerator", "ICollection", "IList", "IDictionary", "ISet",
        "ILookup", "IReadOnlyCollection", "IReadOnlyDictionary", "IReadOnlyList", "IReadOnlySet",
        "Array",
    };

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var method = (MethodDeclarationSyntax)ctx.Node;
        if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return;
        if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) return;

        if (method.ReturnType is not NullableTypeSyntax nullableType) return;
        if (IsContainerType(nullableType.ElementType)) return;
        if (IsValueType(ctx.SemanticModel, nullableType.ElementType, ctx.CancellationToken)) return;

        var methodName = method.Identifier.ValueText;
        if (methodName.StartsWith("Try", StringComparison.Ordinal)) return;
        if (method.ParameterList.Parameters.Any(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword)))) return;

        var body = GetMethodBody(method);
        if (body is null) return;
        if (!HasDirectReturnNull(body)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, method.Identifier.GetLocation(), methodName));
    }

    private static SyntaxNode? GetMethodBody(MethodDeclarationSyntax method) {
        if (method.Body is not null) return method.Body;
        if (method.ExpressionBody is not null) return method.ExpressionBody.Expression;
        return null;
    }

    private static bool HasDirectReturnNull(SyntaxNode body) {
        if (body is ReturnStatementSyntax ret && IsNullLiteral(ret.Expression))
            return true;

        foreach (var descendant in body.DescendantNodes()) {
            if (descendant is not ReturnStatementSyntax returnStmt) continue;
            if (returnStmt.Expression is null) continue;
            if (IsNullLiteral(returnStmt.Expression) && !IsInNestedFunction(returnStmt, body))
                return true;
        }
        return false;
    }

    private static bool IsInNestedFunction(SyntaxNode node, SyntaxNode outerBody) {
        var current = node.Parent;
        while (current is not null && current != outerBody) {
            if (current is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
                return true;
            current = current.Parent;
        }
        return false;
    }

    private static bool IsNullLiteral(ExpressionSyntax? expr) {
        if (expr is null) return false;
        if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression))
            return true;
        if (expr is PostfixUnaryExpressionSyntax postfix &&
            postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) &&
            postfix.Operand is LiteralExpressionSyntax operandLiteral &&
            operandLiteral.IsKind(SyntaxKind.NullLiteralExpression))
            return true;
        return false;
    }

    private static bool IsValueType(SemanticModel semanticModel, TypeSyntax typeSyntax, CancellationToken ct) {
        var symbol = semanticModel.GetTypeInfo(typeSyntax, ct).Type;
        return symbol is { IsValueType: true };
    }

    private static bool IsContainerType(TypeSyntax typeSyntax) {
        return typeSyntax switch {
            GenericNameSyntax generic => ContainerTypeNames.Contains(generic.Identifier.ValueText),
            IdentifierNameSyntax identifier => ContainerTypeNames.Contains(identifier.Identifier.ValueText),
            QualifiedNameSyntax qualified => IsContainerType(qualified.Right),
            AliasQualifiedNameSyntax alias => IsContainerType(alias.Name),
            ArrayTypeSyntax => true,
            _ => false,
        };
    }
}
