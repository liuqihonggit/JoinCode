namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3003: Process deadlock — WaitForExitAsync before ReadToEndAsync。
/// </summary>
[AnalyzerRule(
    Id = "JCC3003",
    Title = "Process deadlock: WaitForExitAsync before ReadToEndAsync",
    Description = "Calling '{0}' after WaitForExitAsync may cause deadlock. When child process output exceeds pipe buffer size, WaitForExitAsync blocks waiting for process exit while the process blocks waiting for pipe read, forming a deadlock. Correct pattern: start ReadToEndAsync first, then await WaitForExitAsync.",
    Category = "ProcessSafety",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "Process pipe buffer is limited. If child process output exceeds buffer size and is not consumed, the child process blocks on write. If the parent process is waiting on WaitForExitAsync, both sides wait on each other causing deadlock. Start ReadToEndAsync before WaitForExitAsync to avoid this.")]
public sealed class ProcessDeadlockRule : AnalyzerRuleBase<ProcessDeadlockRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.AwaitExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        var awaitExpr = (AwaitExpressionSyntax)ctx.Node;

        var invocation = AotSafetyHelpers.FindMethodInvocation(awaitExpr.Expression, "WaitForExitAsync");
        if (invocation is null) return;

        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType;
        if (containingType is null) return;
        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (typeName != "Process") return;

        var processVariableName = AotSafetyHelpers.GetProcessVariableName(invocation);
        if (processVariableName is null) return;

        var awaitStatement = awaitExpr.Parent;
        while (awaitStatement is InvocationExpressionSyntax or MemberAccessExpressionSyntax)
            awaitStatement = awaitStatement.Parent;
        if (awaitStatement is null) return;

        var parentBlock = awaitStatement.Parent;
        if (parentBlock is null) return;

        var awaitSpan = awaitStatement.Span;
        var foundAwaitStatement = false;
        foreach (var child in parentBlock.ChildNodes()) {
            if (!foundAwaitStatement) {
                if (child.Span == awaitSpan) {
                    foundAwaitStatement = true;
                }
                continue;
            }

            var readToEndCall = AotSafetyHelpers.FindReadToEndAsyncCall(child, processVariableName);
            if (readToEndCall is not null) {
                var memberAccess = readToEndCall.Expression as MemberAccessExpressionSyntax;
                var readTarget = memberAccess?.Expression?.ToString() ?? "stream";
                var desc = $"{readTarget}.ReadToEndAsync()";
                ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, readToEndCall.GetLocation(), desc));
            }
        }
    }
}
