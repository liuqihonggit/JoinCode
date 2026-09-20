namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC6010: 代码风格 — LINQ链式语法超过8句应拆分为有名函数。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Performance",
    Id = "JCC6010",
    Title = "代码风格: LINQ链式语法超过8句应拆分为有名函数",
    Description = "LINQ链式表达式中包含 {0} 个方法调用，超过8句上限。应将部分操作提取为有意义命名的函数，主流程保持 <= 8 句链式调用。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "Long LINQ chains reduce readability. Extract part of the chain into a well-named function, keeping the main flow <= 8 chained calls.")]
public sealed class LongLinqChainRule : AnalyzerRuleBase<LongLinqChainRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeLongLinqChain, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeLongLinqChain(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        if (ctx.Node is not InvocationExpressionSyntax invocation) return;

        var chainCount = CountChainedCalls(invocation);
        if (chainCount <= 8) return;

        if (invocation.Parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation(), chainCount));
    }

    private static int CountChainedCalls(InvocationExpressionSyntax invocation) {
        var count = 1;
        var current = invocation.Expression;

        while (current is MemberAccessExpressionSyntax memberAccess) {
            count++;
            if (memberAccess.Expression is InvocationExpressionSyntax innerInvocation) {
                count += CountChainedCalls(innerInvocation) - 1;
                break;
            }
            if (memberAccess.Expression is MemberAccessExpressionSyntax innerMemberAccess) {
                break;
            }
            break;
        }

        return count;
    }
}
