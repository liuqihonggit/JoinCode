namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC10010: 代码规范 — [McpToolParameter] 描述文本应使用 WellKnownParam 枚举替代手写字符串。
/// 检测描述文本与 WellKnownParam 的 [ParamMeta] 描述匹配时，建议改用 [McpToolParameter(WellKnownParam.X)]。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10010",
    Title = "代码规范: McpToolParameter 描述应使用 WellKnownParam 枚举",
    Description = "参数描述 '{0}' 与 WellKnownParam.{1} 的 [ParamMeta] 描述重复。应改用 [McpToolParameter(WellKnownParam.{1})] 替代手写字符串，确保参数描述单一数据源。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "公共参数描述必须用 WellKnownParam 枚举定义. 用 [McpToolParameter(WellKnownParam.X)] 替代 [McpToolParameter(\"描述文本\")] 可确保: 1) 描述文本单一数据源; 2) 编译期校验一致性; 3) 消除重复. ADR: 0135")]
public sealed class WellKnownParamStringRule : AnalyzerRuleBase<WellKnownParamStringRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var descriptionToEnumName = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        foreach (var tree in context.Compilation.SyntaxTrees) {
            if (context.CancellationToken.IsCancellationRequested) return;

            var root = tree.GetRoot();

            foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>()) {
                var attrName = attr.Name.ToString().Replace(" ", "");
                if (attrName != "ParamMeta" && attrName != "ParamMetaAttribute") continue;

                var args = attr.ArgumentList?.Arguments;
                if (args is null || args.Value.Count == 0) continue;

                var firstArg = args.Value[0].Expression;
                if (firstArg is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                    continue;

                var description = literal.Token.ValueText;

                var parentEnum = attr.Parent?.Parent;
                if (parentEnum is EnumMemberDeclarationSyntax enumMember) {
                    descriptionToEnumName.TryAdd(description, enumMember.Identifier.ValueText);
                }
            }
        }

        if (descriptionToEnumName.IsEmpty) return;

        context.RegisterSyntaxTreeAction(treeCtx => {
            if (treeCtx.CancellationToken.IsCancellationRequested) return;
            var root = treeCtx.Tree.GetRoot();

            foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>()) {
                var attrName = attr.Name.ToString().Replace(" ", "");
                if (attrName != "McpToolParameter" && attrName != "McpToolParameterAttribute") continue;

                var args = attr.ArgumentList?.Arguments;
                if (args is null || args.Value.Count == 0) continue;

                var firstArg = args.Value[0].Expression;
                if (firstArg is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                    continue;

                var description = literal.Token.ValueText;
                if (descriptionToEnumName.TryGetValue(description, out var enumName)) {
                    var location = attr.GetLocation();
                    treeCtx.ReportDiagnostic(Diagnostic.Create(
                        Descriptor,
                        location,
                        description,
                        enumName));
                }
            }
        });
    }
}
