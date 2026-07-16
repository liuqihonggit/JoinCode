namespace AotSafety.Generator
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DisposableConsistencyRules : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor RuleDualDisposable = new(
            "JCC9102",
            "Disposable 一致性: 类型同时实现 IDisposable 和 IAsyncDisposable — 应二选一",
            "类型 '{0}' 同时实现 IDisposable 和 IAsyncDisposable — 应二选一。项目统一模式：接口层用 IAsyncDisposable，消费方用 await using",
            "DisposableConsistency",
            DiagnosticSeverity.Error,
            true,
            "A type implementing both IDisposable and IAsyncDisposable confuses consumers: use 'using' or 'await using'?" +
            "Project convention: 1) implement only IAsyncDisposable; 2) DisposeAsync() contains actual cleanup logic; 3) consumers use 'await using'." +
            "Exception: framework-mandated dual implementation (e.g. Stream subclasses) requires a comment explaining why.");

        private static readonly DiagnosticDescriptor RuleTrivialAsyncDispose = new(
            "JCC9103",
            "Disposable 一致性: DisposeAsync() 仅委托给 Dispose() — 应统一为 IAsyncDisposable + await using，删除冗余的 IDisposable",
            "类型 '{0}' 同时实现 IDisposable 和 IAsyncDisposable，但 DisposeAsync() 仅委托给 Dispose() — 应统一为 IAsyncDisposable + await using，删除冗余的 IDisposable",
            "DisposableConsistency",
            DiagnosticSeverity.Warning,
            true,
            "When DisposeAsync() merely calls Dispose() and returns ValueTask.CompletedTask, there is no real async cleanup logic." +
            "Correct approach: 1) remove the IDisposable interface declaration; 2) inline Dispose() logic into DisposeAsync(); 3) consumers switch to 'await using'." +
            "This eliminates dual-interface ambiguity and prevents misuse of synchronous Dispose.");

        private static readonly DiagnosticDescriptor RuleSyncUsingOnAsyncDisposable = new(
            "JCC9104",
            "Disposable 一致性: IAsyncDisposable 类型应使用 await using 而非 using",
            "类型 '{0}' 实现 IAsyncDisposable，应使用 'await using' 而非 'using'。using（同步）只调用 Dispose()，不会调用 DisposeAsync()，可能跳过异步清理逻辑。",
            "DisposableConsistency",
            DiagnosticSeverity.Warning,
            true,
            "When a type implements only IAsyncDisposable, 'using var' (synchronous) attempts to call Dispose(), but the type lacks Dispose(), causing the compiler to fall back to DisposeAsync().GetAwaiter().GetResult()," +
            "which may deadlock in synchronous contexts. Correct approach: always use 'await using var'.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(RuleDualDisposable, RuleTrivialAsyncDispose, RuleSyncUsingOnAsyncDisposable);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
        }

        private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var typeDecl = (TypeDeclarationSyntax)ctx.Node;
            var symbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
            if (symbol is null) return;

            var idisposableType = ctx.Compilation.GetTypeByMetadataName("System.IDisposable");
            var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
            if (idisposableType is null || iasyncDisposableType is null) return;

            var implementsIDisposable = symbol.AllInterfaces.Contains(idisposableType, SymbolEqualityComparer.Default);
            var implementsIAsyncDisposable = symbol.AllInterfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);

            if (implementsIDisposable && implementsIAsyncDisposable)
            {
                var hasRealAsyncDispose = HasRealAsyncDispose(symbol);
                var typeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                ctx.ReportDiagnostic(Diagnostic.Create(
                    hasRealAsyncDispose ? RuleDualDisposable : RuleTrivialAsyncDispose,
                    typeDecl.Identifier.GetLocation(),
                    typeName));
            }
        }

        private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var localDecl = (LocalDeclarationStatementSyntax)ctx.Node;

            if (!localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) return;
            if (localDecl.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)) return;

            foreach (var variable in localDecl.Declaration.Variables)
            {
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

                if (implementsIAsyncDisposable && !implementsIDisposable)
                {
                    var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        RuleSyncUsingOnAsyncDisposable,
                        localDecl.UsingKeyword.GetLocation(),
                        typeName));
                }
            }
        }

        private static bool HasRealAsyncDispose(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers("DisposeAsync"))
            {
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
}
