namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3021: _ = Task.Run(...) 应改用 FireAndForgetTask.Run 或 FireAndForgetTracker.Register。
/// 统一 fire-and-forget 基础设施（ADR 0155）— 裸跑 Task.Run 无 Dispose 守卫，异常静默丢失。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3021",
    Title = "即发即忘: _ = Task.Run 应改用 FireAndForgetTask.Run 或 FireAndForgetTracker.Register",
    Description = "_ = Task.Run(...) 裸跑无 Dispose 守卫，进程退出时资源泄漏，异常静默丢失。改用 FireAndForgetTask.Run(factory, name) 获得自带 CancellationToken + Dispose 守卫 + 可选 await；或 tracker.Register(factory) 注册到容器统一管理。",
    Category = "FireAndForgetSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Bare _ = Task.Run(...) has no Dispose guard — resources leak on process exit, exceptions silently lost. Fix: 1) use FireAndForgetTask.Run(factory, name) for self-contained CTS + Dispose guard + optional await; 2) or tracker.Register(factory) for container-managed lifecycle. See ADR 0155.")]
public sealed class BareTaskRunRule : AnalyzerRuleBase<BareTaskRunRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ExpressionStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var exprStatement = (ExpressionStatementSyntax)ctx.Node;

        if (exprStatement.Expression is not AssignmentExpressionSyntax assignment)
            return;
        if (!assignment.Left.IsKind(SyntaxKind.IdentifierName))
            return;
        var leftIdentifier = (IdentifierNameSyntax)assignment.Left;
        if (leftIdentifier.Identifier.ValueText != "_")
            return;

        var taskRunInvocation = FindTaskRun(assignment.Right, ctx.SemanticModel);
        if (taskRunInvocation is null) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, taskRunInvocation.GetLocation()));
    }

    private static InvocationExpressionSyntax? FindTaskRun(ExpressionSyntax expr, SemanticModel semanticModel) {
        if (expr is InvocationExpressionSyntax invocation) {
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is not null && symbol.ContainingType?.Name == "Task" && symbol.Name == "Run")
                return invocation;

            foreach (var inner in expr.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                var innerSymbol = semanticModel.GetSymbolInfo(inner).Symbol as IMethodSymbol;
                if (innerSymbol is not null && innerSymbol.ContainingType?.Name == "Task" && innerSymbol.Name == "Run")
                    return inner;
            }
        }

        return null;
    }
}
