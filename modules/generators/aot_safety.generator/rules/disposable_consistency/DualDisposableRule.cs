namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9102/JCC9103: Disposable 一致性 — 类型同时实现 IDisposable 和 IAsyncDisposable。
/// 多描述符规则 — 共享 AnalyzeTypeDeclaration 逻辑(根据 hasRealAsyncDispose 选择报告哪个)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9102",
    Title = "Disposable 一致性: 类型同时实现 IDisposable 和 IAsyncDisposable — 应二选一",
    Description = "类型 '{0}' 同时实现 IDisposable 和 IAsyncDisposable — 应二选一。项目统一模式：接口层用 IAsyncDisposable，消费方用 await using",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "A type implementing both IDisposable and IAsyncDisposable confuses consumers: use 'using' or 'await using'?" +
    "Project convention: 1) implement only IAsyncDisposable; 2) DisposeAsync() contains actual cleanup logic; 3) consumers use 'await using'." +
    "Exception: framework-mandated dual implementation (e.g. Stream subclasses) requires a comment explaining why.")]
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9103",
    Title = "Disposable 一致性: DisposeAsync() 仅委托给 Dispose() — 应统一为 IAsyncDisposable + await using，删除冗余的 IDisposable",
    Description = "类型 '{0}' 同时实现 IDisposable 和 IAsyncDisposable，但 DisposeAsync() 仅委托给 Dispose() — 应统一为 IAsyncDisposable + await using，删除冗余的 IDisposable",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "When DisposeAsync() merely calls Dispose() and returns ValueTask.CompletedTask, there is no real async cleanup logic." +
    "Correct approach: 1) remove the IDisposable interface declaration; 2) inline Dispose() logic into DisposeAsync(); 3) consumers switch to 'await using'." +
    "This eliminates dual-interface ambiguity and prevents misuse of synchronous Dispose.")]
public sealed class DualDisposableRule : IAnalyzerRule {
    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> Map = RuleDescriptorFactory.CreateAll<DualDisposableRule>();
    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = Map.Values.ToList();

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration,
            SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var typeDecl = (TypeDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (symbol is null) return;

        if (symbol.IsAbstract) return;

        var idisposableType = ctx.Compilation.GetTypeByMetadataName("System.IDisposable");
        var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
        if (idisposableType is null || iasyncDisposableType is null) return;

        var implementsIDisposable = symbol.Interfaces.Contains(idisposableType, SymbolEqualityComparer.Default);
        var implementsIAsyncDisposable = symbol.Interfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);

        if (implementsIDisposable && implementsIAsyncDisposable) {
            var hasRealAsyncDispose = HasRealAsyncDispose(symbol);
            var typeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            ctx.ReportDiagnostic(Diagnostic.Create(
                hasRealAsyncDispose ? Map["JCC9102"] : Map["JCC9103"],
                typeDecl.Identifier.GetLocation(),
                typeName));
        }
    }

    private static bool HasRealAsyncDispose(INamedTypeSymbol type) {
        foreach (var member in type.GetMembers("DisposeAsync")) {
            if (member is not IMethodSymbol method) continue;

            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef is null) continue;

            var node = syntaxRef.GetSyntax();
            var block = node.DescendantNodes().OfType<BlockSyntax>().FirstOrDefault();
            if (block is null) continue;

            var bodyText = block.ToString();

            var isTrivialDelegate =
                bodyText.Contains("Dispose();") &&
                bodyText.Contains("ValueTask.CompletedTask") &&
                !bodyText.Contains("await ");

            if (!isTrivialDelegate)
                return true;
        }

        return false;
    }
}
