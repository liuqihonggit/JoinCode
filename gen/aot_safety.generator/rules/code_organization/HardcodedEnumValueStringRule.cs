namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC10007: 代码规范 — 禁止硬编码 [EnumValue] 定义的字符串常量。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10007",
    Title = "代码规范: 禁止硬编码 [EnumValue] 定义的字符串常量",
    Description = "字符串字面量 '{0}' 与枚举 '{1}' 的 [EnumValue] 值重复。应通过 XxxExtensions.ToValue()/FromValue() 或 XxxConstants 获取，禁止在消费方重复硬编码相同字符串。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "枚举是唯一数据源. 字符串值由 [EnumValue] 定义一次，所有消费方通过 ToValue()/FromValue()/XxxConstants 获取. 在消费方重复硬编码相同字符串会导致: 1) 枚举值变更时多处同步修改; 2) 拼写错误无法编译期检测; 3) 违反 DRY 原则. 例外: 外部协议字符串、日志/异常消息中的描述性文本.")]
public sealed class HardcodedEnumValueStringRule : AnalyzerRuleBase<HardcodedEnumValueStringRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var enumValueStrings = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        foreach (var tree in context.Compilation.SyntaxTrees) {
            if (context.CancellationToken.IsCancellationRequested) return;

            var root = tree.GetRoot();

            foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>()) {
                var attrName = attr.Name.ToString().Replace(" ", "");
                if (attrName != "EnumValue" && attrName != "EnumValueAttribute") continue;

                var args = attr.ArgumentList?.Arguments;
                if (args is null || args.Value.Count == 0) continue;

                var firstArg = args.Value[0].Expression;
                if (firstArg is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                    continue;

                var stringValue = literal.Token.ValueText;

                var parentEnum = attr.Parent?.Parent;
                if (parentEnum is EnumMemberDeclarationSyntax enumMember) {
                    var enumType = enumMember.Parent as EnumDeclarationSyntax;
                    var enumName = enumType?.Identifier.ValueText ?? "Unknown";
                    enumValueStrings.TryAdd(stringValue, enumName);
                }
            }
        }

        if (enumValueStrings.IsEmpty) return;

        context.RegisterSyntaxNodeAction(nodeCtx => {
            if (nodeCtx.CancellationToken.IsCancellationRequested) return;

            if (nodeCtx.Node is not LiteralExpressionSyntax literal ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression))
                return;

            var stringValue = literal.Token.ValueText;

            if (!enumValueStrings.TryGetValue(stringValue, out var enumName)) return;

            if (IsInEnumDefinition(literal)) return;

            var location = literal.GetLocation();
            nodeCtx.ReportDiagnostic(Diagnostic.Create(Descriptor, location, stringValue, enumName));
        }, SyntaxKind.StringLiteralExpression);
    }

    private static bool IsInEnumDefinition(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is EnumDeclarationSyntax or EnumMemberDeclarationSyntax)
                return true;
            if (current is AttributeArgumentSyntax)
                return true;
            if (current is AttributeSyntax)
                return true;
            current = current.Parent;
        }
        return false;
    }
}
