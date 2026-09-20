namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9201/JCC9202: Dispose 幂等守卫 — Dispose/DisposeAsync 方法体内必须有幂等守卫防止重复释放。
/// 多描述符规则 — 共享 AnalyzeDisposeGuard 逻辑(根据 isSyncDispose 选择报告哪个)。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9201",
    Title = "Dispose 幂等守卫: Dispose() 方法体内必须有幂等守卫防止重复释放",
    Description = "类型 '{0}' 实现 IDisposable 但 Dispose() 缺少幂等守卫(Interlocked.Exchange/CompareExchange 或 if(_disposed) return)。无守卫时重复调用 Dispose 会重复执行清理逻辑,导致 double-free/资源泄露/竞态条件。",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "AGENTS.md: Dispose 必须幂等。正确模式: 1) 'if (Interlocked.Exchange(ref _disposed, 1) == 1) return;' 或 " +
    "2) 'if (_disposed) return; _disposed = true;' (非线程安全但也是守卫)。" +
    "例外: sealed 类 + 单次调用场景(如 using var)可豁免,但建议统一加守卫.")]
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9202",
    Title = "Dispose 幂等守卫: DisposeAsync() 方法体内必须有幂等守卫防止重复释放",
    Description = "类型 '{0}' 实现 IAsyncDisposable 但 DisposeAsync() 缺少幂等守卫(Interlocked.Exchange/CompareExchange 或 if(_disposed) return)。无守卫时重复调用 DisposeAsync 会重复执行清理逻辑,导致 double-free/资源泄露/竞态条件。",
    Category = "DisposableConsistency",
    Severity = DiagnosticSeverity.Error,
    IsEnabledByDefault = true,
    HelpLinkUri = "AGENTS.md: DisposeAsync 必须幂等。正确模式: 1) 'if (Interlocked.Exchange(ref _disposed, 1) == 1) return;' 或 " +
    "2) 'if (_disposed) return; _disposed = true;' (非线程安全但也是守卫)。" +
    "例外: sealed 类 + 单次调用场景(如 await using var)可豁免,但建议统一加守卫.")]
public sealed class DisposeGuardRule : IAnalyzerRule {
    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> Map = RuleDescriptorFactory.CreateAll<DisposeGuardRule>();
    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = Map.Values.ToList();

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeDisposeGuard, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeDisposeGuard(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;
        var methodName = methodDecl.Identifier.ValueText;

        var isSyncDispose = methodName == "Dispose" && methodDecl.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword };
        var isAsyncDispose = methodName == "DisposeAsync";
        if (!isSyncDispose && !isAsyncDispose) return;

        var body = methodDecl.Body;
        if (body is null) return;

        var typeDecl = methodDecl.Parent as TypeDeclarationSyntax;
        if (typeDecl is null) return;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (symbol is null) return;
        if (symbol.IsAbstract) return;

        if (!HasInstanceFields(symbol)) return;

        if (isSyncDispose) {
            var idisposableType = ctx.Compilation.GetTypeByMetadataName("System.IDisposable");
            if (idisposableType is null) return;
            var implementsIDisposable = symbol.Interfaces.Contains(idisposableType, SymbolEqualityComparer.Default);
            if (!implementsIDisposable) return;
        } else {
            var iasyncDisposableType = ctx.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
            if (iasyncDisposableType is null) return;
            var implementsIAsyncDisposable = symbol.Interfaces.Contains(iasyncDisposableType, SymbolEqualityComparer.Default);
            if (!implementsIAsyncDisposable) return;
        }

        if (HasDisposeGuard(body)) return;
        if (HasDelegateGuard(body, ctx.SemanticModel, symbol)) return;

        var typeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        ctx.ReportDiagnostic(Diagnostic.Create(
            isSyncDispose ? Map["JCC9201"] : Map["JCC9202"],
            methodDecl.Identifier.GetLocation(),
            typeName));
    }

    private static bool HasInstanceFields(INamedTypeSymbol type) {
        foreach (var member in type.GetMembers()) {
            if (member is IFieldSymbol { IsStatic: false } field) {
                if (field.IsConst) continue;
                if (field.IsImplicitlyDeclared) continue;
                return true;
            }
        }
        return false;
    }

    private static bool HasDisposeGuard(BlockSyntax body) {
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (invocation.Expression is MemberAccessExpressionSyntax ma) {
                var methodName = ma.Name.Identifier.ValueText;
                if (methodName is "Exchange" or "CompareExchange") {
                    var receiverName = ma.Expression switch {
                        IdentifierNameSyntax id => id.Identifier.ValueText,
                        MemberAccessExpressionSyntax inner => inner.Name.Identifier.ValueText,
                        _ => null
                    };
                    if (receiverName is "Interlocked" or "Volatile") return true;
                }
            }
        }

        foreach (var ifStmt in body.DescendantNodes().OfType<IfStatementSyntax>()) {
            var hasReturn = ifStmt.Statement is ReturnStatementSyntax ||
                (ifStmt.Statement is BlockSyntax b && b.Statements.Count == 1 && b.Statements[0] is ReturnStatementSyntax);
            if (hasReturn) return true;
        }

        return false;
    }

    private static bool HasDelegateGuard(BlockSyntax body, SemanticModel semanticModel, INamedTypeSymbol currentType) {
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (IsInsideLambdaOrLocalFunction(invocation, body)) continue;

            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol calledMethod) continue;

            if (!SymbolEqualityComparer.Default.Equals(calledMethod.ContainingType, currentType)) continue;

            foreach (var decl in calledMethod.DeclaringSyntaxReferences) {
                if (decl.GetSyntax() is MethodDeclarationSyntax { Body: { } calleeBody }) {
                    if (HasDisposeGuard(calleeBody)) return true;
                }
            }
        }
        return false;
    }

    private static bool IsInsideLambdaOrLocalFunction(SyntaxNode node, BlockSyntax methodBody) {
        var current = node.Parent;
        while (current is not null && current != methodBody) {
            if (current is SimpleLambdaExpressionSyntax or
                ParenthesizedLambdaExpressionSyntax or
                LocalFunctionStatementSyntax)
                return true;
            current = current.Parent;
        }
        return false;
    }
}
