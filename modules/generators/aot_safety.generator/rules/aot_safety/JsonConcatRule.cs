namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC1017: AOT/JSON 安全 — 禁止手写 JSON 字符串拼接。
/// 0 容忍: 必须用 DTO + JsonContext 双向转换(JsonSerializer.Serialize/Deserialize)。
/// 检测三种模式:
///   1. EscapeJsonString 方法调用 — 手写 JSON 转义
///   2. StringBuilder.Append/AppendLine 参数含 JSON 特征字符({" 或 ": 或 "}) — StringBuilder 拼 JSON
///   3. 内插字符串同时含 {{ 和 }} 和 ": — $"{{...}}" 造 JSON
/// 例外: GraphQL 用 BuildGraphQL 分层(外层 DTO + 内层查询字符串), 内层 $"..." 不在此检测范围(无 {{ }} 同时出现)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "JsonSerializerAot",
    Id = "JCC1017",
    Title = "AOT/JSON: 禁止手写 JSON 字符串拼接",
    Description = "检测到手写 JSON 拼接模式 '{0}'。必须用 DTO + JsonContext 双向转换(JsonSerializer.Serialize/Deserialize), 禁止 StringBuilder 拼 JSON / $\"{{}}\" 内插造 JSON / EscapeJsonString 手写转义。理由: 手写 JSON 易出错(转义/编码/格式), 无法 AOT 优化, 难以维护。",
    Category = "AotSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Hand-written JSON concatenation is prohibited. Use DTO + JsonContext (JsonSerializer.Serialize/Deserialize) instead. Detected patterns: 1) EscapeJsonString() call; 2) StringBuilder.Append with JSON literal ({\" or \": or \"}); 3) Interpolated string with {{ }} and \": (JSON braces + key-value separator). Rationale: hand-written JSON is error-prone (escaping/encoding/format), cannot be AOT-optimized, and is hard to maintain. GraphQL BuildGraphQL inner query strings are exempt (no {{ }} co-occurrence).")]
public sealed class JsonConcatRule : AnalyzerRuleBase<JsonConcatRule> {
    private static readonly HashSet<string> StringBuilderAppendMethods = new(StringComparer.Ordinal)
    {
        "Append", "AppendLine", "Insert",
    };

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInterpolatedString, SyntaxKind.InterpolatedStringExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var inv = (InvocationExpressionSyntax)ctx.Node;

        if (IsEscapeJsonStringCall(inv)) {
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, inv.GetLocation(), "EscapeJsonString()"));
            return;
        }

        if (IsStringBuilderAppendJson(inv)) {
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, inv.GetLocation(), "StringBuilder.Append(JSON)"));
            return;
        }
    }

    private static void AnalyzeInterpolatedString(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var interp = (InterpolatedStringExpressionSyntax)ctx.Node;
        var fullText = interp.ToString();
        if (!fullText.Contains("{{") || !fullText.Contains("}}")) return;
        if (!fullText.Contains("\":")) return;
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, interp.GetLocation(), "$\"{{...}}\" 内插造 JSON"));
    }

    private static bool IsEscapeJsonStringCall(InvocationExpressionSyntax inv) {
        return inv.Expression switch {
            IdentifierNameSyntax id => id.Identifier.ValueText == "EscapeJsonString",
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText == "EscapeJsonString",
            _ => false,
        };
    }

    private static bool IsStringBuilderAppendJson(InvocationExpressionSyntax inv) {
        if (inv.Expression is not MemberAccessExpressionSyntax ma) return false;
        if (!StringBuilderAppendMethods.Contains(ma.Name.Identifier.ValueText)) return false;
        if (!IsStringBuilderExpression(ma.Expression)) return false;

        foreach (var arg in inv.ArgumentList.Arguments) {
            if (ContainsJsonLiteral(arg.Expression)) return true;
        }
        return false;
    }

    private static bool IsStringBuilderExpression(ExpressionSyntax expr) {
        return expr switch {
            IdentifierNameSyntax id => id.Identifier.ValueText == "sb"
                                     || id.Identifier.ValueText == "builder"
                                     || id.Identifier.ValueText == "jsonBuilder"
                                     || id.Identifier.ValueText == "jsonSb",
            MemberAccessExpressionSyntax ma when ma.Name is IdentifierNameSyntax propId =>
                propId.Identifier.ValueText == "StringBuilder" || IsStringBuilderExpression(ma.Expression),
            _ => false,
        };
    }

    private static bool ContainsJsonLiteral(ExpressionSyntax expr) {
        if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) {
            var text = literal.Token.ValueText.AsSpan();
            return ContainsJsonFeature(text);
        }
        return false;
    }

    private static bool ContainsJsonFeature(ReadOnlySpan<char> text) {
        return text.Contains("{\"".AsSpan(), StringComparison.Ordinal)
            || text.Contains("\":".AsSpan(), StringComparison.Ordinal)
            || text.Contains("\"}".AsSpan(), StringComparison.Ordinal)
            || text.Contains(",\"".AsSpan(), StringComparison.Ordinal);
    }
}
