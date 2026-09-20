namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9301/JCC9302: IDisposable 字段未在 Dispose 中释放 / 可空字段释放后未置 null。
/// 多描述符规则 — 共享 AnalyzeDisposableFields 逻辑。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9301",
    Title = "内存泄漏: IDisposable字段未在Dispose/DisposeAsync中释放",
    Description = "字段 '{0}' 类型实现了 IDisposable/IAsyncDisposable，但未在 Dispose/DisposeAsync 方法中释放。将导致资源泄漏。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "字段类型实现了 IDisposable/IAsyncDisposable, 必须在 Dispose/DisposeAsync 方法中释放. 正确做法: 1) 在 Dispose() 中调用 field.Dispose() 或 field.DisposeSafe(_logger); 2) 在 DisposeAsync() 中调用 await field.DisposeAsync() 或 await field.DisposeSafeAsync(_logger); 3) 可空字段释放后应置 null 防止 use-after-free.",
    IsCompilationEnd = true)]
[AnalyzerRule(
    AnalyzerId = "MemoryLeak",
    Id = "JCC9302",
    Title = "内存泄漏: 可空IDisposable字段释放后未置null",
    Description = "可空字段 '{0}' 在Dispose/DisposeAsync中释放后未置null。置null可防止use-after-free并明确表达释放意图。",
    Category = "ResourceSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "可空IDisposable字段在Dispose中释放后应置null. 正确做法: field?.Dispose(); field = null; 或 await (field?.DisposeAsync() ?? ValueTask.CompletedTask); field = null;. 置null可: 1) 防止use-after-free; 2) 明确表达释放意图; 3) 帮助检测重复释放.",
    IsCompilationEnd = true)]
public sealed class DisposableFieldReleaseRule : IAnalyzerRule {
    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> Map = RuleDescriptorFactory.CreateAll<DisposableFieldReleaseRule>();
    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = Map.Values.ToList();

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        var disposableFields = new ConcurrentDictionary<IFieldSymbol, Location>(SymbolEqualityComparer.Default);
        var idisposableType = context.Compilation.GetTypeByMetadataName("System.IDisposable");
        var iasyncDisposableType = context.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");

        context.RegisterSyntaxNodeAction(ctx => {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            var fieldDecl = (FieldDeclarationSyntax)ctx.Node;
            if (fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) return;

            var fieldType = ctx.SemanticModel.GetTypeInfo(fieldDecl.Declaration.Type, ctx.CancellationToken).Type;
            if (fieldType is null) return;
            if (!IsDisposableType(fieldType, idisposableType, iasyncDisposableType)) return;
            if (IsExcludedType(fieldType)) return;

            foreach (var variable in fieldDecl.Declaration.Variables) {
                if (variable.Initializer is null) continue;
                if (AotSafetyHelpers.IsNullLiteral(variable.Initializer.Value)) continue;

                var symbol = ctx.SemanticModel.GetDeclaredSymbol(variable, ctx.CancellationToken) as IFieldSymbol;
                if (symbol is null) continue;
                if (symbol.IsImplicitlyDeclared) continue;
                if (symbol.IsStatic) continue;

                disposableFields.TryAdd(symbol, variable.Identifier.GetLocation());
            }
        }, SyntaxKind.FieldDeclaration);

        context.RegisterCompilationEndAction(ctx => {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            foreach (var kvp in disposableFields) {
                if (ctx.CancellationToken.IsCancellationRequested) return;

                var field = kvp.Key;
                var location = kvp.Value;

                var containingType = field.ContainingType;
                if (containingType is null) continue;
                if (!IsDisposableType(containingType, idisposableType, iasyncDisposableType)) continue;

                var releasedInDispose = AotSafetyHelpers.CheckInDisposeCallChain(containingType,
                    m => AotSafetyHelpers.MethodBodyReferencesField(m, field.Name));

                if (!releasedInDispose) {
                    ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9301"], location, field.Name));
                } else if (field.NullableAnnotation == NullableAnnotation.Annotated && !field.IsReadOnly) {
                    var nulledInDispose = AotSafetyHelpers.CheckInDisposeCallChain(containingType,
                        m => AotSafetyHelpers.MethodBodyNullsField(m, field.Name));

                    if (!nulledInDispose) {
                        ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC9302"], location, field.Name));
                    }
                }
            }
        });
    }

    private static bool IsDisposableType(ITypeSymbol type, INamedTypeSymbol? idisposable, INamedTypeSymbol? iasyncDisposable) {
        if (type is not INamedTypeSymbol namedType) return false;

        if (idisposable is not null && namedType.AllInterfaces.Contains(idisposable, SymbolEqualityComparer.Default))
            return true;
        if (iasyncDisposable is not null && namedType.AllInterfaces.Contains(iasyncDisposable, SymbolEqualityComparer.Default))
            return true;
        if (idisposable is not null && SymbolEqualityComparer.Default.Equals(namedType, idisposable))
            return true;
        if (iasyncDisposable is not null && SymbolEqualityComparer.Default.Equals(namedType, iasyncDisposable))
            return true;

        return false;
    }

    private static bool IsExcludedType(ITypeSymbol type) {
        var nameSpan = type.Name.AsSpan();
        if (nameSpan.SequenceEqual("SemaphoreSlim".AsSpan())) return true;
        if (nameSpan.SequenceEqual("ConcurrentDictionary".AsSpan())) return true;
        return false;
    }
}
