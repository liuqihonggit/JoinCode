namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3013: 禁止空 catch 块。
/// </summary>
[AnalyzerRule(
    Id = "JCC3013",
    Title = "代码规范: 禁止空 catch 块",
    Description = "空 catch 块会隐藏异常，导致难以调试的问题。catch 块必须至少写一行日志（如 _logger.LogWarning(ex, \"...\") 或 Console.WriteLine(\"...\")），绝不允许留空。即使异常可以忽略，也必须记录日志以便排查。唯一例外: catch(OperationCanceledException) 或 catch(TaskCanceledException) 允许留空。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "Empty catch blocks silently swallow exceptions, making bugs impossible to diagnose. You MUST add at least one logging line (e.g. _logger.LogWarning(ex, \"...\") or Console.WriteLine(\"...\")). Even if the exception is ignorable, logging is mandatory for debugging. Exception: catch(OperationCanceledException) or catch(TaskCanceledException) may be empty.")]
public sealed class EmptyCatchBlockRule : AnalyzerRuleBase<EmptyCatchBlockRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.CatchClause);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        var catchClause = (CatchClauseSyntax)ctx.Node;
        var block = catchClause.Block;
        if (block is null) return;
        if (block.Statements.Count > 0) return;

        if (catchClause.Declaration is not null) {
            var exceptionType = ctx.SemanticModel.GetTypeInfo(catchClause.Declaration.Type, ctx.CancellationToken).Type;
            if (exceptionType is not null) {
                var name = exceptionType.Name;
                if (name is "OperationCanceledException" or "TaskCanceledException")
                    return;
            }
        }

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, catchClause.CatchKeyword.GetLocation()));
    }
}
