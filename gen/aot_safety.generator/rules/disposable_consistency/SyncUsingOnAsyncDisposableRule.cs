namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9104: Disposable 一致性 — IAsyncDisposable 类型应使用 await using 而非 using。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9104",
    Title = "Disposable 一致性: IAsyncDisposable 类型应使用 await using 而非 using",
    Description = "类型 '{0}' 实现 IAsyncDisposable，应使用 'await using' 而非 'using'。using（同步）只调用 Dispose()，不会调用 DisposeAsync()，可能跳过异步清理逻辑。",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "When a type implements only IAsyncDisposable, 'using var' (synchronous) attempts to call Dispose(), but the type lacks Dispose(), causing the compiler to fall back to DisposeAsync().GetAwaiter().GetResult()," +
    "which may deadlock in synchronous contexts. Correct approach: always use 'await using var'.")]
public sealed class SyncUsingOnAsyncDisposableRule : AnalyzerRuleBase<SyncUsingOnAsyncDisposableRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var localDecl = (LocalDeclarationStatementSyntax)ctx.Node;

        if (!localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) return;
        if (localDecl.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)) return;

        foreach (var variable in localDecl.Declaration.Variables) {
            if (variable.Initializer is null) continue;

            var typeInfo = ctx.SemanticModel.GetTypeInfo(variable.Initializer.Value, ctx.CancellationToken);
            var type = typeInfo.Type as INamedTypeSymbol;
            if (type is null) continue;

            var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
            if (iasyncDisposableType is null) return;

            var idisposableType = ctx.Compilation.GetTypeByMetadataName("System.IDisposable");
            if (idisposableType is null) return;

            var implementsIAsyncDisposable = type.AllInterfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);
            var implementsIDisposable = type.AllInterfaces.Contains(idisposableType, SymbolEqualityComparer.Default);

            // 只报告纯 IAsyncDisposable(不实现 IDisposable)的类型
            // 双接口类型(IDisposable + IAsyncDisposable)用同步 using 是合法的
            if (implementsIAsyncDisposable && !implementsIDisposable) {
                var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                ctx.ReportDiagnostic(Diagnostic.Create(
                    Descriptor,
                    localDecl.UsingKeyword.GetLocation(),
                    typeName));
            }
        }
    }

    private static bool IsWhitelistedDualInterface(INamedTypeSymbol type) {
        var fullName = type.OriginalDefinition.ToDisplayString();
        return fullName is "System.Threading.CancellationTokenSource"
            or "System.IO.MemoryStream"
            or "System.Threading.CancellationTokenRegistration";
    }
}
