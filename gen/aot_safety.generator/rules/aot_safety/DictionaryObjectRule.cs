namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC1001/JCC1002/JCC1003: Dictionary&lt;string, object?&gt; 在 NativeAOT 下不安全。
/// 多描述符规则 — 3 个 descriptor 对应 nullable/non-nullable/inherits 三种情况。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC1001",
    Title = "AOT incompatible: Dictionary<string, object?> is unsafe under NativeAOT",
    Description = "Type '{0}' uses Dictionary<string, object?> which cannot be safely serialized under NativeAOT. Use Dictionary<string, JsonElement> or a strongly-typed alternative instead.",
    Category = "AotSafety",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "NativeAOT requires all serialized types to be determined at compile time. The object? value type cannot satisfy this requirement.")]
public sealed class DictionaryObjectRule : IAnalyzerRule {
    private static readonly DiagnosticDescriptor RuleNullable = RuleDescriptorFactory.Create<DictionaryObjectRule>();

    private static readonly DiagnosticDescriptor RuleNonNullable = new(
        "JCC1002",
        "AOT incompatible: Dictionary<string, object> is unsafe under NativeAOT",
        "Type '{0}' uses Dictionary<string, object> which cannot be safely serialized under NativeAOT. Use Dictionary<string, JsonElement> or a strongly-typed alternative instead.",
        "AotSafety",
        DiagnosticSeverity.Warning,
        true,
        "NativeAOT requires all serialized types to be determined at compile time. The object value type cannot satisfy this requirement.");

    private static readonly DiagnosticDescriptor RuleInherits = new(
        "JCC1003",
        "AOT incompatible: Type inherits from Dictionary<string, object?>",
        "Type '{0}' inherits from Dictionary<string, object?> which cannot be safely serialized under NativeAOT. Use composition with Dictionary<string, JsonElement> or a strongly-typed wrapper instead.",
        "AotSafety",
        DiagnosticSeverity.Warning,
        true,
        "Inheriting from Dictionary<string, object?> makes the entire type unsafe for AOT serialization.");

    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = new[] { RuleNullable, RuleNonNullable, RuleInherits };

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.VariableDeclaration,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.FieldDeclaration,
            SyntaxKind.Parameter);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        switch (ctx.Node) {
            case PropertyDeclarationSyntax prop:
            CheckTypeSymbol(ctx, prop.Type, prop.Identifier.GetLocation());
            break;
            case FieldDeclarationSyntax field:
            foreach (var v in field.Declaration.Variables)
                CheckTypeSymbol(ctx, field.Declaration.Type, v.Identifier.GetLocation());
            break;
            case ParameterSyntax param:
            CheckTypeSymbol(ctx, param.Type, param.Identifier.GetLocation());
            break;
            case VariableDeclarationSyntax varDecl:
            foreach (var v in varDecl.Variables)
                CheckTypeSymbol(ctx, varDecl.Type, v.Identifier.GetLocation());
            break;
            case ObjectCreationExpressionSyntax obj:
            CheckTypeSymbol(ctx, obj.Type, obj.Type.GetLocation());
            break;
        }
    }

    private static void CheckTypeSymbol(SyntaxNodeAnalysisContext ctx, TypeSyntax? typeSyntax, Location location) {
        if (typeSyntax is null) return;

        var symbol = ctx.SemanticModel.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
        if (symbol is null) return;

        if (IsDictionaryStringObject(symbol)) {
            var isNullable = symbol.TypeArguments.Length >= 2 &&
                symbol.TypeArguments[1].IsReferenceType;

            var rule = isNullable ? RuleNullable : RuleNonNullable;
            var displayStr = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            ctx.ReportDiagnostic(Diagnostic.Create(rule, location, displayStr));
        }

        if (symbol.BaseType is not null && IsDictionaryStringObject(symbol.BaseType)) {
            var displayStr = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            ctx.ReportDiagnostic(Diagnostic.Create(RuleInherits, location, displayStr));
        }
    }

    private static bool IsDictionaryStringObject(INamedTypeSymbol type) {
        if (!type.IsGenericType) return false;

        var def = type.ConstructedFrom;
        if (def is null) return false;

        var fullName = $"{def.ContainingNamespace?.ToDisplayString()}.{def.Name}";
        if (fullName != "System.Collections.Generic.Dictionary") return false;

        if (type.TypeArguments.Length != 2) return false;
        if (type.TypeArguments[0].SpecialType != SpecialType.System_String) return false;
        if (type.TypeArguments[1].SpecialType != SpecialType.System_Object) return false;

        return true;
    }
}
