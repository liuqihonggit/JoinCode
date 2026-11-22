namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC1010: switch 判断字符串应使用枚举+特性描述替代。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC1010",
    Title = "代码风格: switch 判断字符串应使用枚举+特性描述替代",
    Description = "switch 语句判断 string 类型是魔法字符串模式。应定义枚举 + [EnumValue] 特性，用枚举 switch 替代字符串 switch，提高类型安全性和可维护性。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Info,
    HelpLinkUri = "String switch is a magic string pattern. Define an enum with [EnumValue] attributes and use enum switch instead for type safety and maintainability.")]
public sealed class SwitchOnStringRule : AnalyzerRuleBase<SwitchOnStringRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.SwitchStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        if (ctx.Node is not SwitchStatementSyntax switchStmt) return;

        var switchExprType = ctx.SemanticModel.GetTypeInfo(switchStmt.Expression, ctx.CancellationToken).Type;
        if (switchExprType is null) return;
        if (switchExprType.SpecialType != SpecialType.System_String) return;

        var caseCount = switchStmt.Sections.Sum(s => s.Labels.Count(l => l is CaseSwitchLabelSyntax));
        if (caseCount <= 2) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, switchStmt.Expression.GetLocation()));
    }
}
