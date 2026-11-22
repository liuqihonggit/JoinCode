namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC10005: 代码规范 — 有限集合的字符串常量应枚举化。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10005",
    Title = "代码规范: 有限集合的字符串常量应枚举化",
    Description = "switch/match 表达式判断字符串 '{0}' 是魔法字符串模式。应定义枚举 + [EnumValue] 特性，用枚举 switch 替代字符串 switch，提高类型安全性和可维护性。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "有限集合的字符串常量（模型名、角色名、状态名等）必须枚举化. 正确做法: 1) 定义枚举 + [EnumValue] 特性; 2) 利用源码生成器自动生成 XxxConstants + XxxExtensions; 3) 用枚举 switch 替代字符串 switch. 例外: 外部 API 的动态字符串、用户输入的任意字符串、仅使用一次的常量.")]
public sealed class StringConstantMustBeEnumRule : AnalyzerRuleBase<StringConstantMustBeEnumRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeStringMatchExpression, SyntaxKind.SwitchExpression);
    }

    private static void AnalyzeStringMatchExpression(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var switchExpr = (SwitchExpressionSyntax)ctx.Node;

        var governingType = ctx.SemanticModel.GetTypeInfo(switchExpr.GoverningExpression).Type;
        if (governingType is null || governingType.SpecialType != SpecialType.System_String) return;

        if (switchExpr.Arms.Count < 3) return;

        var location = switchExpr.GoverningExpression.GetLocation();
        var exprText = switchExpr.GoverningExpression.ToString();
        if (exprText.Length > 30) exprText = exprText.Substring(0, 30) + "...";

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, location, exprText));
    }
}
