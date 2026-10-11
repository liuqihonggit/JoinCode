namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC4005: 资源泄漏 — SemaphoreSlim 字段未在 Dispose 中释放。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC4005",
    Title = "资源泄漏: SemaphoreSlim 字段 '{0}' 未在 Dispose 中释放",
    Description = "SemaphoreSlim 字段 '{0}' 未在 Dispose/DisposeAsync 方法中调用 Dispose(). SemaphoreSlim 持有内核句柄，未释放将导致句柄泄漏.",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "SemaphoreSlim 持有 ManualResetEvent 内核句柄.如果类实现了 IDisposable 但未在 Dispose 中释放 SemaphoreSlim 字段，将导致内核句柄泄漏.正确模式: 在 Dispose() 中调用 _semaphore.Dispose().如果类有多个 SemaphoreSlim，逐一释放.",
    IsCompilationEnd = true)]
public sealed class SemaphoreSlimNotDisposedRule : AnalyzerRuleBase<SemaphoreSlimNotDisposedRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var semaphoreSlimFields = new ConcurrentDictionary<IFieldSymbol, Location>(SymbolEqualityComparer.Default);

        context.RegisterSyntaxNodeAction(ctx => {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var fieldDecl = (FieldDeclarationSyntax)ctx.Node;

            var fieldType = ctx.SemanticModel.GetTypeInfo(fieldDecl.Declaration.Type, ctx.CancellationToken).Type;
            if (fieldType is null) return;
            if (fieldType.Name != "SemaphoreSlim") return;

            foreach (var variable in fieldDecl.Declaration.Variables) {
                var symbol = ctx.SemanticModel.GetDeclaredSymbol(variable, ctx.CancellationToken) as IFieldSymbol;
                if (symbol is null) continue;

                if (symbol.IsStatic) continue;

                semaphoreSlimFields.TryAdd(symbol, variable.Identifier.GetLocation());
            }
        }, SyntaxKind.FieldDeclaration);

        context.RegisterCompilationEndAction(ctx => {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            foreach (var kvp in semaphoreSlimFields) {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var field = kvp.Key;
                var location = kvp.Value;

                var containingType = field.ContainingType;
                if (containingType is null) continue;

                var implementsDisposable = containingType.AllInterfaces.Any(i =>
                    i.Name == "IDisposable" &&
                    i.ContainingNamespace?.ToDisplayString() == "System");

                if (!implementsDisposable) continue;

                var disposedInDispose = IsFieldDisposedInMethod(containingType, field.Name, "Dispose") ||
                                        IsFieldDisposedInMethod(containingType, field.Name, "DisposeAsync") ||
                                        IsFieldDisposedInMethod(containingType, field.Name, "OnDispose");

                if (!disposedInDispose) {
                    ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, location, field.Name));
                }
            }
        });
    }

    private static bool IsFieldDisposedInMethod(INamedTypeSymbol type, string fieldName, string methodName) {
        foreach (var refDecl in type.DeclaringSyntaxReferences) {
            var syntax = refDecl.GetSyntax();
            if (syntax is not TypeDeclarationSyntax typeDecl) continue;

            var disposeMethods = typeDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ValueText == methodName);

            foreach (var method in disposeMethods) {
                var hasFieldReference = false;

                if (method.Body is not null) {
                    hasFieldReference = method.Body.DescendantNodes()
                        .OfType<IdentifierNameSyntax>()
                        .Any(id => id.Identifier.ValueText == fieldName);
                } else if (method.ExpressionBody is not null) {
                    hasFieldReference = method.ExpressionBody.Expression.DescendantNodesAndSelf()
                        .OfType<IdentifierNameSyntax>()
                        .Any(id => id.Identifier.ValueText == fieldName);
                }

                if (hasFieldReference) return true;
            }
        }

        return false;
    }
}
