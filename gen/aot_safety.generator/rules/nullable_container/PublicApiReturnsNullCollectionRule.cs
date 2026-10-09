namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC11004: 可空性 — 公开 API 禁止返回 null 集合，必须返回空集合。
/// 0 容忍: public 方法/属性返回集合类型时，方法体内出现 return null 即报。
/// 即使返回类型标注为 List&lt;T&gt;? 也不允许 — 空集合比 null 更健壮。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "NullableContainer",
    Id = "JCC11004",
    Title = "可空性: 公开 API 禁止返回 null 集合",
    Description = "公开方法 '{0}' 返回集合类型但有 return null。空集合比 null 更健壮: 无需 null 检查, 遍历空集合是 no-op, JSON 反序列化默认空集合。改为 return Array.Empty<T>() 或 return new List<T>() 或同等空集合表达式。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Public APIs returning null collections force every caller to null-check. Empty collections are safer: 1) foreach over empty is a no-op; 2) .Count == 0 works without null check; 3) JSON deserialization defaults to empty. This rule fires on any return null in a public method/property whose return type is a collection, even if the type is nullable (List<T>?). Zero-tolerance: prefer empty over null.")]
public sealed class PublicApiReturnsNullCollectionRule : AnalyzerRuleBase<PublicApiReturnsNullCollectionRule> {
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
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var method = (MethodDeclarationSyntax)ctx.Node;
        if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return;
        if (!IsContainerType(method.ReturnType)) return;
        var body = GetMethodBody(method);
        if (body is null) return;
        if (!HasDirectReturnNull(body)) return;
        var methodName = method.Identifier.ValueText;
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, method.Identifier.GetLocation(), methodName));
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var prop = (PropertyDeclarationSyntax)ctx.Node;
        if (!prop.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return;
        if (prop.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) return;
        if (!IsContainerType(prop.Type)) return;
        if (prop.AccessorList is null) return;
        foreach (var accessor in prop.AccessorList.Accessors) {
            if (!accessor.Keyword.IsKind(SyntaxKind.GetKeyword)) continue;
            SyntaxNode? body = accessor.Body;
            if (body is null && accessor.ExpressionBody is not null)
                body = accessor.ExpressionBody.Expression;
            if (body is null) continue;
            if (!HasDirectReturnNull(body)) continue;
            var propName = prop.Identifier.ValueText;
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, prop.Identifier.GetLocation(), propName));
            return;
        }
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

    private static bool IsContainerType(TypeSyntax typeSyntax) {
        return typeSyntax switch {
            NullableTypeSyntax nullable => IsContainerType(nullable.ElementType),
            GenericNameSyntax generic => ContainerTypeNames.Contains(generic.Identifier.ValueText),
            IdentifierNameSyntax identifier => ContainerTypeNames.Contains(identifier.Identifier.ValueText),
            QualifiedNameSyntax qualified => IsContainerType(qualified.Right),
            AliasQualifiedNameSyntax alias => IsContainerType(alias.Name),
            ArrayTypeSyntax => true,
            _ => false,
        };
    }
}
