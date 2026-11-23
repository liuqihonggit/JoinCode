namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC1004: dynamic 关键字在 NativeAOT 下不支持。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC1004",
    Title = "AOT incompatible: dynamic 关键字在 NativeAOT 下不支持",
    Description = "dynamic 关键字依赖运行时动态分发，NativeAOT 不支持。使用具体类型、JsonElement 或源码生成器替代。",
    Category = "AotSafety",
    Severity = DiagnosticSeverity.Error,
    HelpLinkUri = "dynamic 关键字依赖 DLR (Dynamic Language Runtime) 进行运行时方法解析. NativeAOT 编译时需要确定所有类型, DLR 的 CallSite 缓存和后期绑定机制无法在 AOT 环境中工作. 替代方案: 1) 使用具体类型; 2) 使用 JsonElement 处理动态 JSON; 3) 使用源码生成器在编译期生成代码.")]
public sealed class DynamicKeywordRule : AnalyzerRuleBase<DynamicKeywordRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IdentifierName);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not IdentifierNameSyntax identifier) return;
        if (identifier.Identifier.ValueText != "dynamic") return;

        var parent = identifier.Parent;
        if (parent is null) return;

        if (parent is VariableDeclarationSyntax ||
            parent is ParameterSyntax ||
            parent is TypeArgumentListSyntax ||
            parent is GenericNameSyntax ||
            parent is PredefinedTypeSyntax ||
            parent is ArrayTypeSyntax ||
            parent is NullableTypeSyntax ||
            parent is CastExpressionSyntax ||
            parent is ObjectCreationExpressionSyntax ||
            parent is TypeOfExpressionSyntax ||
            parent is DefaultExpressionSyntax ||
            parent is SizeOfExpressionSyntax ||
            parent is PointerTypeSyntax ||
            parent is FunctionPointerParameterSyntax ||
            parent is DeclarationPatternSyntax ||
            parent is RecursivePatternSyntax) {
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, identifier.GetLocation()));
            return;
        }

        if (parent is MethodDeclarationSyntax methodDecl && methodDecl.ReturnType == identifier) {
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, identifier.GetLocation()));
            return;
        }

        if (parent is VariableDeclarationSyntax varDecl && varDecl.Type == identifier) {
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, identifier.GetLocation()));
        }
    }
}
