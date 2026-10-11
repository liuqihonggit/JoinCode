namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC10006: 代码规范 — 禁止手动维护 Key==Value 的冗余映射字典。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10006",
    Title = "代码规范: 禁止手动维护 Key==Value 的冗余映射字典",
    Description = "字典 '{0}' 的 Key 和 Value 完全相同，是冗余映射。应使用枚举数组 + ToValue() 遍历匹配，或直接用枚举 + [EnumValue] 特性由源码生成器自动生成映射。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "当字典的 Key == Value 时（如 \"gpt-4o\" → \"gpt-4o\"），手动维护映射是冗余的. 正确做法: 1) 定义枚举 + [EnumValue] 特性; 2) 用 EnumType[] + ToValue() 遍历匹配; 3) 源码生成器自动生成 FrozenDictionary 映射. 例外: Key != Value 的映射字典是合法的.")]
public sealed class RedundantKeyValueDictionaryRule : AnalyzerRuleBase<RedundantKeyValueDictionaryRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeRedundantKeyValueDictionary, SyntaxKind.FieldDeclaration, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeRedundantKeyValueDictionary(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        TypeSyntax? typeSyntax = null;
        VariableDeclaratorSyntax? variableDeclarator = null;
        string? variableName = null;

        switch (ctx.Node) {
            case FieldDeclarationSyntax field:
            typeSyntax = field.Declaration.Type;
            variableDeclarator = field.Declaration.Variables.FirstOrDefault();
            break;
            case PropertyDeclarationSyntax property:
            typeSyntax = property.Type;
            variableName = property.Identifier.ValueText;
            break;
        }

        if (typeSyntax is null) return;

        var typeSymbol = ctx.SemanticModel.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
        if (typeSymbol is null) return;

        if (!IsDictionaryStringString(typeSymbol)) return;

        if (variableDeclarator is not null)
            variableName = variableDeclarator.Identifier.ValueText;

        if (string.IsNullOrEmpty(variableName)) return;

        var hasRedundantKV = false;
        var initializer = variableDeclarator?.Initializer?.Value;

        if (initializer is ObjectCreationExpressionSyntax objCreation &&
            objCreation.ArgumentList?.Arguments.Count == 0) {
            if (objCreation.Initializer is not null) {
                foreach (var init in objCreation.Initializer.Expressions) {
                    if (init is InitializerExpressionSyntax collectionInit) {
                        foreach (var item in collectionInit.Expressions) {
                            if (IsKeyValueSame(item)) {
                                hasRedundantKV = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        if (!hasRedundantKV) return;

        var location = ctx.Node.GetLocation();
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, location, variableName));
    }

    private static bool IsDictionaryStringString(INamedTypeSymbol type) {
        if (!type.IsGenericType) return false;
        var def = type.ConstructedFrom;
        if (def is null) return false;
        var fullName = $"{def.ContainingNamespace?.ToDisplayString()}.{def.Name}";
        if (fullName != "System.Collections.Generic.Dictionary") return false;
        if (type.TypeArguments.Length != 2) return false;
        return type.TypeArguments[0].SpecialType == SpecialType.System_String &&
               type.TypeArguments[1].SpecialType == SpecialType.System_String;
    }

    private static bool IsKeyValueSame(ExpressionSyntax expression) {
        if (expression is not ParenthesizedLambdaExpressionSyntax lambda) return false;

        if (lambda.ParameterList.Parameters.Count != 2) return false;

        if (lambda.Body is not InvocationExpressionSyntax invocation) return false;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 2) return false;

        var keyExpr = args[0].Expression.ToString().Trim();
        var valueExpr = args[1].Expression.ToString().Trim();

        return keyExpr == valueExpr;
    }
}
