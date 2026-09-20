namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC10008: 代码规范 — 属性禁止返回 new 表达式。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10008",
    Title = "代码规范: 属性禁止返回 new 表达式",
    Description = "属性 '{0}' 返回 new 表达式，每次访问都会创建新对象。属性应快速、无副作用、可重复调用返回相同结果。请改为: 1) 方法（如 GetXxx()）如果语义是工厂；2) 缓存字段（如 _cachedXxx ??= BuildXxx()）如果语义是延迟计算。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Property access should be fast, side-effect-free, and return the same result on repeated calls (given the same state). Returning new violates these conventions: 1) each access allocates a new object causing GC pressure; 2) multiple accesses return different instances violating intuition; 3) layout engine and other high-frequency access scenarios suffer performance degradation. Exceptions: Array.Empty<T>() and other cached factory methods, record With method return values.")]
public sealed class PropertyReturnsNewRule : AnalyzerRuleBase<PropertyReturnsNewRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzePropertyReturnsNew, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzePropertyReturnsNew(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var prop = (PropertyDeclarationSyntax)ctx.Node;

        if (prop.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            return;

        if (prop.ExplicitInterfaceSpecifier is not null)
            return;

        if (prop.Parent is InterfaceDeclarationSyntax)
            return;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(prop, ctx.CancellationToken);
        if (symbol is not null) {
            if (symbol.ExplicitInterfaceImplementations.Length > 0)
                return;

            if (ImplementsInterfaceProperty(symbol))
                return;
        }

        if (prop.ExpressionBody is not null) {
            if (ContainsNewExpression(prop.ExpressionBody.Expression)) {
                var propName = prop.Identifier.ValueText;
                ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, prop.Identifier.GetLocation(), propName));
            }
            return;
        }

        if (prop.AccessorList is null) return;

        foreach (var accessor in prop.AccessorList.Accessors) {
            if (!accessor.Keyword.IsKind(SyntaxKind.GetKeyword)) continue;

            if (accessor.ExpressionBody is not null) {
                if (ContainsNewExpression(accessor.ExpressionBody.Expression)) {
                    var propName = prop.Identifier.ValueText;
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, prop.Identifier.GetLocation(), propName));
                    return;
                }
            } else if (accessor.Body is not null) {
                foreach (var stmt in accessor.Body.Statements) {
                    if (stmt is ReturnStatementSyntax returnStmt && returnStmt.Expression is not null) {
                        if (ContainsNewExpression(returnStmt.Expression)) {
                            var propName = prop.Identifier.ValueText;
                            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, prop.Identifier.GetLocation(), propName));
                            return;
                        }
                    }
                }
            }
        }
    }

    private static bool ContainsNewExpression(ExpressionSyntax expression) {
        if (expression is ObjectCreationExpressionSyntax)
            return true;

        if (expression is ImplicitObjectCreationExpressionSyntax)
            return true;

        if (expression is InvocationExpressionSyntax invocation) {
            var name = invocation.Expression.ToString();
            if (name.StartsWith("Array.Empty") || name.StartsWith("Array.Empty<"))
                return false;

            foreach (var arg in invocation.ArgumentList.Arguments) {
                if (ContainsNewExpression(arg.Expression))
                    return true;
            }
            return false;
        }

        switch (expression) {
            case BinaryExpressionSyntax binary:
            return ContainsNewExpression(binary.Left) || ContainsNewExpression(binary.Right);
            case ConditionalExpressionSyntax conditional:
            return ContainsNewExpression(conditional.WhenTrue) || ContainsNewExpression(conditional.WhenFalse);
            case SwitchExpressionSyntax switchExpr:
            foreach (var arm in switchExpr.Arms) {
                if (ContainsNewExpression(arm.Expression))
                    return true;
            }
            return false;
            case ParenthesizedExpressionSyntax parenthesized:
            return ContainsNewExpression(parenthesized.Expression);
            case CastExpressionSyntax cast:
            return ContainsNewExpression(cast.Expression);
            case InitializerExpressionSyntax initializer:
            foreach (var expr in initializer.Expressions) {
                if (ContainsNewExpression(expr))
                    return true;
            }
            return false;
            default:
            return false;
        }
    }

    private static bool ImplementsInterfaceProperty(IPropertySymbol property) {
        var containingType = property.ContainingType;
        if (containingType is null) return false;

        foreach (var iface in containingType.AllInterfaces) {
            foreach (var member in iface.GetMembers(property.Name)) {
                if (member is IPropertySymbol)
                    return true;
            }
        }

        return false;
    }
}
