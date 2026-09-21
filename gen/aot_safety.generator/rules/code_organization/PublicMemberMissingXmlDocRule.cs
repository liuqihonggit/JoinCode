namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC10004: 代码规范 — 公开成员必须包含 XML 文档注释。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10004",
    Title = "代码规范: 公开成员必须包含 XML 文档注释",
    Description = "公开成员 '{0}' 缺少 XML 文档注释。所有 public 方法、属性和构造函数都应提供 <summary> 注释，以确保 IntelliSense 信息完整。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Public members should have XML documentation comments. This ensures IntelliSense provides meaningful descriptions and serves as code contract documentation. Exceptions: override members, constructors with no parameters, and private/internal members.")]
public sealed class PublicMemberMissingXmlDocRule : AnalyzerRuleBase<PublicMemberMissingXmlDocRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (projectContext.IsTest) return;
        context.RegisterSyntaxNodeAction(AnalyzePublicMemberXmlDoc,
            SyntaxKind.MethodDeclaration, SyntaxKind.PropertyDeclaration, SyntaxKind.ConstructorDeclaration);
    }

    private static void AnalyzePublicMemberXmlDoc(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        ISymbol? symbol = null;
        Location? location = null;
        string? memberName = null;

        switch (ctx.Node) {
            case MethodDeclarationSyntax method:
            symbol = ctx.SemanticModel.GetDeclaredSymbol(method, ctx.CancellationToken);
            if (symbol is null) return;
            if (symbol.DeclaredAccessibility != Accessibility.Public) return;
            if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;
            if (HasXmlDoc(method)) return;
            location = method.Identifier.GetLocation();
            memberName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            break;

            case PropertyDeclarationSyntax property:
            symbol = ctx.SemanticModel.GetDeclaredSymbol(property, ctx.CancellationToken);
            if (symbol is null) return;
            if (symbol.DeclaredAccessibility != Accessibility.Public) return;
            if (property.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;
            if (HasXmlDoc(property)) return;
            location = property.Identifier.GetLocation();
            memberName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            break;

            case ConstructorDeclarationSyntax ctor:
            symbol = ctx.SemanticModel.GetDeclaredSymbol(ctor, ctx.CancellationToken);
            if (symbol is null) return;
            if (symbol.DeclaredAccessibility != Accessibility.Public) return;
            if (ctor.ParameterList.Parameters.Count == 0) return;
            if (HasXmlDoc(ctor)) return;
            location = ctor.Identifier.GetLocation();
            memberName = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            break;

            default:
            return;
        }

        if (memberName is not null && location is not null)
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, location, memberName));
    }

    private static bool HasXmlDoc(MemberDeclarationSyntax member) {
        foreach (var trivia in member.GetLeadingTrivia()) {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                return true;
        }
        var leadingText = member.GetLeadingTrivia().ToString();
        if (leadingText.Contains("///") || leadingText.Contains("/**"))
            return true;
        return false;
    }
}
