namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC4002: 死锁风险 — SemaphoreSlim.Wait() 无超时保护。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC4002",
    Title = "死锁风险: SemaphoreSlim.Wait() 无超时保护",
    Description = "SemaphoreSlim.Wait() 调用无超时参数. 如果信号量永远不释放，调用将无限阻塞. 应使用 WaitAsync(timeout) 或 Wait(timeout) 并处理超时.",
    Category = "DeadlockSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "无超时的 Wait 调用可能在信号量持有者异常退出时永远阻塞.正确模式: await semaphore.WaitAsync(timeout).ConfigureAwait(false) 或 semaphore.Wait(timeout)，并处理 OperationCanceledException/TimeoutException.")]
public sealed class SemaphoreSlimWaitNoTimeoutRule : AnalyzerRuleBase<SemaphoreSlimWaitNoTimeoutRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeSemaphoreSlimWaitNoTimeout, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeSemaphoreSlimWaitNoTimeout(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (methodName != "Wait" && methodName != "WaitAsync") return;

        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType;
        if (containingType is null) return;
        if (containingType.Name != "SemaphoreSlim") return;

        if (symbol.Parameters.Length == 0) return;

        var hasTimeoutOrCancellationParam = symbol.Parameters.Any(p => {
            var paramTypeName = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return paramTypeName == "int" || paramTypeName == "TimeSpan" || paramTypeName == "CancellationToken";
        });

        if (!hasTimeoutOrCancellationParam) {
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation()));
        }
    }
}
