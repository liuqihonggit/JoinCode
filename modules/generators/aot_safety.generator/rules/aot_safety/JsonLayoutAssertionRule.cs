namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9311: 测试断言 — 禁止对 JSON 字符串做排版敏感断言。
/// <para>检测测试项目中 FluentAssertions <c>Should().Contain(...)</c> / xunit <c>Assert.Contains(...)</c>
/// 的字符串字面量含 JSON 排版特征 <c>"key": value</c>（引号+冒号+空格）。</para>
/// <para>理由: DTO 序列化(JCC1017)输出为紧凑格式且非 ASCII 转义，排版断言会随序列化实现变化集体红
/// （TASK099 实测 Desktop 场景 9 例）；应改用 Testing.Common.JsonAssert 按路径/值语义断言。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "JsonSerializerAot",
    Id = "JCC9311",
    Title = "测试断言: 禁止 JSON 排版敏感断言",
    Description = "检测到对 JSON 字符串的排版敏感断言 '{0}'。改用 Testing.Common.JsonAssert 按路径/值语义断言。理由: DTO 序列化为紧凑格式且非 ASCII 转义, 排版断言会随时序化实现变化导致测试集体红。",
    Category = "AotSafety",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "Avoid layout-sensitive assertions on JSON strings (for example Should().Contain(\"\\\"key\\\": value\") with a space after the colon). Use Testing.Common.JsonAssert to assert semantic paths/values instead. Rationale: DTO serialization emits compact JSON with escaped non-ASCII, so layout assertions break whenever serialization details change.")]
public sealed class JsonLayoutAssertionRule : AnalyzerRuleBase<JsonLayoutAssertionRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (!projectContext.IsTest) return;
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // FluentAssertions 是 Contain, xunit 是 Assert.Contains
    private static bool IsAssertContain(IMethodSymbol symbol) => symbol.ContainingType?.Name switch {
        "StringAssertions" => symbol.Name == "Contain",
        "Assert" => symbol.Name == "Contains",
        _ => false
    };

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var inv = (InvocationExpressionSyntax)ctx.Node;
        if (inv.ArgumentList.Arguments.Count == 0) return;

        var symbol = ctx.SemanticModel.GetSymbolInfo(inv, ctx.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null || !IsAssertContain(symbol)) return;

        foreach (var arg in inv.ArgumentList.Arguments) {
            if (arg.Expression is not LiteralExpressionSyntax literal
                || !literal.IsKind(SyntaxKind.StringLiteralExpression)) {
                continue;
            }
            if (!literal.Token.ValueText.Contains("\": ", StringComparison.Ordinal)) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                Descriptor,
                literal.GetLocation(),
                "Contain(\"<key>\": <value>)"));
            return;
        }
    }
}
