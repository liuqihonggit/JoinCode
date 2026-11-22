namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3004: Process deadlock — RedirectStandardError is true but stderr is never read。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC3004",
    Title = "Process deadlock: RedirectStandardError is true but stderr is never read",
    Description = "Process has RedirectStandardError=true but StandardError is never consumed. When the child process writes enough to stderr to fill the pipe buffer, it will block, potentially causing deadlock if the parent is waiting on WaitForExitAsync.",
    Category = "ProcessSafety",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "When RedirectStandardError is true, the stderr pipe buffer must be consumed. If the child process writes enough data to fill the buffer, it blocks on write. If the parent process is waiting on WaitForExitAsync or reading stdout, both sides wait on each other causing deadlock. Either set RedirectStandardError=false or consume stderr with ReadToEndAsync or BeginErrorReadLine.")]
public sealed class UnreadStderrRule : AnalyzerRuleBase<UnreadStderrRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ObjectCreationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        var objectCreation = (ObjectCreationExpressionSyntax)ctx.Node;

        var typeSymbol = ctx.SemanticModel.GetTypeInfo(objectCreation.Type).Type;
        if (typeSymbol is not null) {
            var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (typeName != "ProcessStartInfo") return;
        } else {
            var typeNameSyntax = objectCreation.Type.ToString();
            if (typeNameSyntax != "ProcessStartInfo") return;
        }

        if (!AotSafetyHelpers.HasRedirectStandardErrorTrue(objectCreation, ctx)) return;

        var enclosingBlock = AotSafetyHelpers.FindEnclosingClassBlock(objectCreation);
        if (enclosingBlock is null) return;

        if (AotSafetyHelpers.HasStandardErrorConsumption(enclosingBlock)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, objectCreation.GetLocation()));
    }
}
