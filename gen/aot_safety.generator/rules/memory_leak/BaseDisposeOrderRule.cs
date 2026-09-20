namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9304: base.Dispose() 必须在 Dispose 方法体最后位置。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9304",
    Title = "释放顺序: base.Dispose() 必须在 Dispose 方法体最后位置",
    Description = "base.{0}() 不在 Dispose 方法体最后位置，其后还有 {1} 条语句。子类资源应先释放，base.Dispose() 最后调用（父类做生命周期注销）。先释放父类会导致子类释放时访问已释放的父类资源。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "base.Dispose()/base.DisposeAsync() must be the last statement in Dispose/DisposeAsync method. Child resources should be released first, then base.Dispose() for parent lifecycle cleanup. If base.Dispose() is called before other statements, those statements may access already-released parent resources.",
    IsCompilationEnd = true)]
public sealed class BaseDisposeOrderRule : AnalyzerRuleBase<BaseDisposeOrderRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;

        var methodName = methodDecl.Identifier.ValueText.AsSpan();
        if (!methodName.SequenceEqual("Dispose".AsSpan()) &&
            !methodName.SequenceEqual("DisposeAsync".AsSpan()))
            return;

        if (!methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;
        if (methodDecl.Body is null) return;

        var statements = methodDecl.Body.Statements;
        if (statements.Count <= 1) return;

        var hasBaseDispose = methodDecl.Body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsBaseDisposeCall);

        if (!hasBaseDispose) return;

        var lastStatement = statements[statements.Count - 1];
        var lastHasBaseDispose = lastStatement.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsBaseDisposeCall);

        if (!lastHasBaseDispose) {
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, lastStatement.GetLocation(), "Dispose", 1));
        }
    }

    private static bool IsBaseDisposeCall(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is not MemberAccessExpressionSyntax ma) return false;
        if (ma.Expression is not BaseExpressionSyntax) return false;

        var name = ma.Name.Identifier.ValueText.AsSpan();
        return name.SequenceEqual("Dispose".AsSpan()) ||
               name.SequenceEqual("DisposeAsync".AsSpan());
    }
}
