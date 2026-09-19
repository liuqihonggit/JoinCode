namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC1006: 方法参数超过8个应封装为类。测试项目豁免。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC1006",
    Title = "代码规范: 方法参数超过8个应封装为类",
    Description = "方法 '{0}' 有 {1} 个参数，超过8个参数应封装为 Options/Request 类。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Info,
    HelpLinkUri = "参数过多降低可读性和可维护性. 正确做法: 1) 封装相关参数为 Options/Request/Config 类; 2) 使用构建器模式; 3) 使用记录类型 (record). 例外: 构造函数、override 方法、接口实现、测试方法、partial 方法.")]
public sealed class TooManyParametersRule : AnalyzerRuleBase<TooManyParametersRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var isTestProject = projectContext.IsTest;
        context.RegisterSyntaxNodeAction(
            ctx => Analyze(ctx, isTestProject),
            SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx, bool isTestProject) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not MethodDeclarationSyntax methodDecl) return;

        var paramCount = methodDecl.ParameterList.Parameters.Count;
        if (paramCount <= 8) return;

        if (methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;
        if (methodDecl.ExplicitInterfaceSpecifier is not null) return;
        if (methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))) return;
        if (isTestProject) return;

        var methodName = methodDecl.Identifier.ValueText;
        if (methodName == "Main") return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, methodDecl.Identifier.GetLocation(), methodName, paramCount));
    }
}
