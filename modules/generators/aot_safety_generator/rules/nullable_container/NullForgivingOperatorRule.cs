namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC11003: 可空性 — 禁止 null-forgiving 运算符 ! 出现在业务代码。
/// 0 容忍: 默认对所有 x! 报 warning。优先用 if (xx is null) return/throw null 检查替代。
/// 理由: ! 压制可空警告等于绕过类型系统，一旦"尽可能"就全盘崩溃。抑制是最后手段，非首选。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "NullableContainer",
    Id = "JCC11003",
    Title = "可空性: 禁止 null-forgiving 运算符 !",
    Description = "null-forgiving 运算符 '{0}!' 压制了可空警告，绕过类型系统。优先用 if (xx is null) return/throw null 检查替代。仅在反射/延迟初始化等确需边界抑制时用 [SuppressMessage] 并注释说明原因，禁止作为常规手段。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "null-forgiving operator ! suppresses nullable warnings, bypassing the type system. Prefer null checks (if (xx is null) return/throw) over suppression. Suppression via [SuppressMessage] is last resort only, not a routine tool.")]
public sealed class NullForgivingOperatorRule : AnalyzerRuleBase<NullForgivingOperatorRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeSuppressNullableWarning, SyntaxKind.SuppressNullableWarningExpression);
    }

    private static void AnalyzeSuppressNullableWarning(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var node = (PostfixUnaryExpressionSyntax)ctx.Node;
        var operandText = node.Operand.ToString();
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, node.OperatorToken.GetLocation(), operandText));
    }
}
