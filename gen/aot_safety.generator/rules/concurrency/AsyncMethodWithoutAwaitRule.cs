namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3017: async 方法无 await — 方法标记 async 但 body 内无任何 await 表达式。
/// 通用检测：纯 AST，不依赖类型/方法名。编译器 CS1998 仅 warning，此规则升级为 error 并给修复提示。
/// 排除：async void 事件处理器（由 JCC3005 管）、body 为表达式体且含 await 的属性/方法。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3017",
    Title = "异步红线: async 方法无 await（async 关键字冗余或遗漏 await）",
    Description = "方法 '{0}' 标记 async 但方法体内无任何 await 表达式。要么遗漏了 await（异步调用被裸丢弃，见 JCC3015），要么 async 关键字冗余（移除 async 避免生成无用状态机）。",
    Category = "AsyncSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "An async method without any await either: 1) forgot to await a Task-returning call (check JCC3015) — add await; 2) has redundant async keyword — remove it to avoid unnecessary state machine generation. CS1998 is a compiler warning; this rule promotes it to a diagnosable issue with fix guidance.")]
public sealed class AsyncMethodWithoutAwaitRule : AnalyzerRuleBase<AsyncMethodWithoutAwaitRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;

        if (!methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) return;

        if (methodDecl.ReturnType is PredefinedTypeSyntax { Keyword: { } kw } && kw.IsKind(SyntaxKind.VoidKeyword)) return;

        if (HasAwaitExpression(methodDecl)) return;

        var methodName = methodDecl.Identifier.ValueText;
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, methodDecl.Identifier.GetLocation(), methodName));
    }

    private static bool HasAwaitExpression(MethodDeclarationSyntax methodDecl) {
        if (methodDecl.ExpressionBody is not null) {
            return methodDecl.ExpressionBody.DescendantNodes().Any(n => n is AwaitExpressionSyntax);
        }
        if (methodDecl.Body is not null) {
            return methodDecl.Body.DescendantNodes().Any(n => n is AwaitExpressionSyntax);
        }
        return false;
    }
}
