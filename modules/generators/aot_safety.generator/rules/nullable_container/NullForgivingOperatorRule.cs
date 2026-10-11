namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC11003: 可空性 — 禁止 null-forgiving 运算符 ! 出现在业务代码。
/// 0 容忍: 默认对所有 x! 报 warning。例外用 #pragma warning disable JCC11003 或 [SuppressMessage] 显式声明并注释说明。
/// 理由: ! 压制可空警告等于绕过类型系统，一旦"尽可能"就全盘崩溃。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "NullableContainer",
    Id = "JCC11003",
    Title = "可空性: 禁止 null-forgiving 运算符 !",
    Description = "null-forgiving 运算符 '{0}!' 压制了可空警告，绕过类型系统。0 容忍策略禁止在业务代码使用。如确需在边界使用，用 #pragma warning disable JCC11003 或 [SuppressMessage] 显式声明并注释说明原因。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "null-forgiving operator ! suppresses nullable warnings, bypassing the type system. Under zero-tolerance policy, it is prohibited in business code. Rationale: once you allow 'as much as possible', the entire nullable type system collapses. To suppress in genuine boundary cases, use #pragma warning disable JCC11003 or [SuppressMessage] with a comment explaining why.")]
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
