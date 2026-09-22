namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3020: 同步方法返回 Task.Run 结果 — 调用方 await 会等待后台任务完成，若 Task.Run 内是无限循环则死锁。
/// 通用检测：基于 BCL Task.Run 判断 + 语义模型返回类型判断，不依赖项目特定方法名。
/// 修复方向：存字段 fire-and-forget + 返回 Task.CompletedTask/ValueTask.CompletedTask，让 DisposeAsync 等待字段 Task。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3020",
    Title = "异步红线: 同步方法返回 Task.Run 结果（await 死锁风险）",
    Description = "同步方法 '{0}' 的 return 语句包含 Task.Run(...) 调用。若 Task.Run 内是无限循环（如 AcceptLoop/PollLoop/ConsumeLoop），调用方 await 会死锁。修复：将 Task 存入字段（供 DisposeAsync 等待），返回 Task.CompletedTask/ValueTask.CompletedTask。",
    Category = "AsyncSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "A non-async method returns Task.Run(...) result. If the Task.Run lambda is an infinite loop (e.g. AcceptLoop/PollLoop/ConsumeLoop), awaiting the returned Task will deadlock. Fix: store the Task in a field (for DisposeAsync to await) and return Task.CompletedTask/ValueTask.CompletedTask instead.")]
public sealed class SyncMethodReturnsTaskRunRule : AnalyzerRuleBase<SyncMethodReturnsTaskRunRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;

        if (methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) return;

        var methodSymbol = ctx.SemanticModel.GetDeclaredSymbol(methodDecl);
        if (methodSymbol is null) return;
        if (!AotSafetyHelpers.IsTaskLikeType(methodSymbol.ReturnType)) return;

        foreach (var returnStmt in methodDecl.DescendantNodes().OfType<ReturnStatementSyntax>()) {
            if (returnStmt.Expression is null) continue;

            if (FindTaskRun(returnStmt.Expression, ctx.SemanticModel) is { } taskRunInvocation) {
                ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, taskRunInvocation.GetLocation(), methodSymbol.Name));
                return;
            }
        }
    }

    private static InvocationExpressionSyntax? FindTaskRun(SyntaxNode expr, SemanticModel semanticModel) {
        foreach (var invocation in expr.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) continue;
            if (symbol.ContainingType?.Name == "Task" && symbol.Name == "Run") return invocation;
        }
        return null;
    }
}
