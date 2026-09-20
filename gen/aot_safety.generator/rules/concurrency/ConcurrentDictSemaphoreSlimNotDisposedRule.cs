namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC4006: 资源泄漏 — ConcurrentDictionary&lt;SemaphoreSlim&gt; 字段未逐个 Dispose。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC4006",
    Title = "资源泄漏: ConcurrentDictionary<SemaphoreSlim> 字段 '{0}' 未逐个 Dispose",
    Description = "ConcurrentDictionary<SemaphoreSlim> 字段 '{0}' 在 Dispose 中仅调用 Clear() 而未逐个 Dispose Value。SemaphoreSlim 持有内核句柄，仅 Clear 会导致句柄泄漏。应先 foreach 逐个 kvp.Value.Dispose()，再 Clear()。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "ConcurrentDictionary.Clear() 仅移除引用，不调用 Value 的 Dispose.SemaphoreSlim 持有 ManualResetEvent 内核句柄，未释放将泄漏.正确模式: foreach (var kvp in _dict) { kvp.Value.Dispose(); } _dict.Clear(); .",
    IsCompilationEnd = true)]
public sealed class ConcurrentDictSemaphoreSlimNotDisposedRule : AnalyzerRuleBase<ConcurrentDictSemaphoreSlimNotDisposedRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var concurrentDictFields = new ConcurrentDictionary<IFieldSymbol, Location>(SymbolEqualityComparer.Default);

        context.RegisterSyntaxNodeAction(ctx => {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var fieldDecl = (FieldDeclarationSyntax)ctx.Node;

            var fieldType = ctx.SemanticModel.GetTypeInfo(fieldDecl.Declaration.Type, ctx.CancellationToken).Type as INamedTypeSymbol;
            if (fieldType is null) return;

            if (fieldType.Name != "ConcurrentDictionary") return;

            if (fieldType.TypeArguments.Length != 2) return;
            if (fieldType.TypeArguments[1].Name != "SemaphoreSlim") return;

            foreach (var variable in fieldDecl.Declaration.Variables) {
                var symbol = ctx.SemanticModel.GetDeclaredSymbol(variable, ctx.CancellationToken) as IFieldSymbol;
                if (symbol is null) continue;

                if (symbol.IsStatic) continue;

                concurrentDictFields.TryAdd(symbol, variable.Identifier.GetLocation());
            }
        }, SyntaxKind.FieldDeclaration);

        context.RegisterCompilationEndAction(ctx => {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            foreach (var kvp in concurrentDictFields) {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var field = kvp.Key;
                var location = kvp.Value;

                var containingType = field.ContainingType;
                if (containingType is null) continue;

                var implementsDisposable = containingType.AllInterfaces.Any(i =>
                    i.Name == "IDisposable" &&
                    i.ContainingNamespace?.ToDisplayString() == "System");

                if (!implementsDisposable) continue;

                var hasForEachDispose = IsFieldDisposedInMethod(containingType, field.Name, "Dispose") ||
                                        IsFieldDisposedInMethod(containingType, field.Name, "DisposeAsync") ||
                                        IsFieldDisposedInMethod(containingType, field.Name, "OnDispose");

                if (!hasForEachDispose) {
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
